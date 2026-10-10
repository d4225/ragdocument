using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.Models;
using UglyToad.PdfPig;

namespace SmartDocumentRAG.API.Services;

public class DocumentProcessingJob : IDocumentProcessorJob
{
    private readonly ApplicationDbContext _db;
    private readonly IAIService _aiService;
    private readonly ILogger<DocumentProcessingJob> _logger;

    public DocumentProcessingJob(
        ApplicationDbContext db,
        IAIService aiService,
        ILogger<DocumentProcessingJob> logger)
    {
        _db = db;
        _aiService = aiService;
        _logger = logger;
    }

    public async Task ProcessPdfAsync(Guid documentId)
    {
        var document = await _db.Documents.FindAsync(documentId);
        if (document == null)
        {
            _logger.LogWarning("Không tìm thấy Document Id: {DocumentId}", documentId);
            return;
        }

        try
        {
            _logger.LogInformation(">>> [Hangfire] Bắt đầu xử lý PDF: {FileName} (Id: {DocumentId})", document.FileName, document.Id);
            document.Status = DocumentStatus.Processing;
            document.ErrorMessage = null;
            await _db.SaveChangesAsync();

            if (!File.Exists(document.FilePath))
            {
                throw new FileNotFoundException($"Không tìm thấy tệp PDF tại: {document.FilePath}");
            }

            using var pdf = PdfDocument.Open(document.FilePath);
            int totalPages = pdf.NumberOfPages;
            _logger.LogInformation("Tài liệu có tổng cộng {TotalPages} trang.", totalPages);

            var parentChunksToInsert = new List<ParentChunk>();
            var childChunksToInsert = new List<ChildChunk>();

            int parentChunkCounter = 0;
            int childChunkCounter = 0;

            foreach (var page in pdf.GetPages())
            {
                int pageNum = page.Number;
                string pageText = page.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    _logger.LogInformation("Trang {PageNum} không có ký tự văn bản, bỏ qua.", pageNum);
                    continue;
                }

                // Chia nội dung trang thành các Parent Chunks (~800 - 1000 từ)
                var parentTexts = SplitIntoParentChunks(pageText, maxWordsPerParent: 800);

                foreach (var pText in parentTexts)
                {
                    parentChunkCounter++;
                    var parentChunk = new ParentChunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = document.Id,
                        Content = pText,
                        PageNumber = pageNum,
                        ChunkIndex = parentChunkCounter
                    };
                    parentChunksToInsert.Add(parentChunk);

                    // Chia nhỏ Parent Chunk thành các Child Chunks (~150 - 200 từ, overlap 20 từ)
                    var childTexts = SplitIntoChildChunks(pText, chunkSize: 180, overlap: 20);

                    foreach (var cText in childTexts)
                    {
                        childChunkCounter++;

                        // Gọi AI Service để sinh vector 768 chiều
                        float[] embeddingArray = await _aiService.GetEmbeddingAsync(cText);

                        var childChunk = new ChildChunk
                        {
                            Id = Guid.NewGuid(),
                            ParentChunkId = parentChunk.Id,
                            Content = cText,
                            Embedding = new Pgvector.Vector(embeddingArray),
                            ChunkIndex = childChunkCounter
                        };
                        childChunksToInsert.Add(childChunk);
                    }
                }
            }

            // Batch Insert toàn bộ ParentChunks và ChildChunks vào PostgreSQL
            _logger.LogInformation("Đang lưu {ParentCount} ParentChunks và {ChildCount} ChildChunks vào database...",
                parentChunksToInsert.Count, childChunksToInsert.Count);

            if (parentChunksToInsert.Count > 0)
            {
                await _db.ParentChunks.AddRangeAsync(parentChunksToInsert);
                await _db.SaveChangesAsync();
            }

            if (childChunksToInsert.Count > 0)
            {
                await _db.ChildChunks.AddRangeAsync(childChunksToInsert);
                await _db.SaveChangesAsync();
            }

            document.Status = DocumentStatus.Completed;
            document.ErrorMessage = null;
            await _db.SaveChangesAsync();

            _logger.LogInformation("<<< [Hangfire] Xử lý hoàn tất thành công cho Document: {FileName}", document.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xử lý file PDF {FileName}: {Message}", document.FileName, ex.Message);
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync();
            throw; // Ném lại để Hangfire ghi nhận thất bại và hỗ trợ retry nếu cần
        }
    }

    /// <summary>
    /// Cắt đoạn lớn Parent (~800 từ)
    /// </summary>
    private static List<string> SplitIntoParentChunks(string text, int maxWordsPerParent)
    {
        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        if (words.Length <= maxWordsPerParent)
        {
            result.Add(text);
            return result;
        }

        for (int i = 0; i < words.Length; i += maxWordsPerParent)
        {
            var chunk = string.Join(" ", words.Skip(i).Take(maxWordsPerParent));
            result.Add(chunk);
        }

        return result;
    }

    /// <summary>
    /// Cắt nhỏ ParentChunk thành các ChildChunk (~150-200 từ, overlap 20 từ)
    /// </summary>
    private static List<string> SplitIntoChildChunks(string text, int chunkSize, int overlap)
    {
        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        if (words.Length <= chunkSize)
        {
            result.Add(text);
            return result;
        }

        int step = Math.Max(1, chunkSize - overlap);
        for (int i = 0; i < words.Length; i += step)
        {
            var chunkWords = words.Skip(i).Take(chunkSize).ToArray();
            if (chunkWords.Length == 0) break;

            result.Add(string.Join(" ", chunkWords));

            if (i + chunkSize >= words.Length)
            {
                break; // Đã đến cuối đoạn
            }
        }

        return result;
    }
}