using System.Text;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.Models;
using SmartDocumentRAG.API.Services;
using UglyToad.PdfPig;

namespace SmartDocumentRAG.API.Services
{
    public class DocumentProcessingJob
    {
        private readonly ApplicationDbContext _db;
        private readonly IGeminiService _geminiService;
        private readonly ILogger<DocumentProcessingJob> _logger;

        public DocumentProcessingJob(
            ApplicationDbContext db,
            IGeminiService geminiService,
            ILogger<DocumentProcessingJob> logger)
        {
            _db = db;
            _geminiService = geminiService;
            _logger = logger;
        }

        public async Task ProcessPdfAsync(Guid documentId)
        {
            var document = await _db.Documents.FindAsync(documentId);
            if (document == null) return;

            try
            {
                _logger.LogInformation("========== BẮT ĐẦU XỬ LÝ & KIỂM DUYỆT TÀI LIỆU ==========");
                _logger.LogInformation("File: {File}", document.FileName);

                // =========================================================================
                // BƯỚC 1: TRÍCH XUẤT TEXT MẪU VÀ KIỂM DUYỆT AN TOÀN NỘI DUNG
                // =========================================================================
                string sampleText = ExtractSampleTextForModeration(document.FilePath, maxPages: 3);

                if (!string.IsNullOrWhiteSpace(sampleText))
                {
                    _logger.LogInformation("Đang gửi văn bản mẫu sang Gemini để kiểm duyệt nội dung...");
                    var (isSafe, reason) = await _geminiService.EvaluateContentSafetyAsync(sampleText);

                    if (!isSafe)
                    {
                        _logger.LogWarning("⚠️ TÀI LIỆU BỊ TỪ CHỐI DO KHÔNG PHÙ HỢP: {Reason}", reason);

                        // Cập nhật trạng thái từ chối
                        document.Status = "Rejected";
                        document.RejectReason = reason;
                        await _db.SaveChangesAsync();

                        // Xóa file vật lý khỏi server để bảo mật
                        if (File.Exists(document.FilePath))
                        {
                            File.Delete(document.FilePath);
                            _logger.LogInformation("Đã xóa file vi phạm khỏi ổ đĩa.");
                        }

                        return; // Dừng xử lý, không vector hóa nữa
                    }

                    _logger.LogInformation("✅ Tài liệu đã vượt qua vòng kiểm duyệt nội dung an toàn.");
                }

                // =========================================================================
                // BƯỚC 2: BÓC TÁCH TOÀN BỘ PDF, CHIA CHUNK & EMBEDDING VECTOR
                // =========================================================================
                using var pdf = PdfDocument.Open(document.FilePath);

                int totalPages = pdf.NumberOfPages;
                _logger.LogInformation("Tổng số trang: {Total}", totalPages);

                int pageNumber = 1;

                foreach (var page in pdf.GetPages())
                {
                    string text = page.Text.Trim();

                    _logger.LogInformation("--------------------------------");
                    _logger.LogInformation("Trang {Page}", pageNumber);
                    _logger.LogInformation("Số ký tự: {Length}", text.Length);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _logger.LogWarning("Trang rỗng, bỏ qua.");
                        pageNumber++;
                        continue;
                    }

                    // ===== Parent Chunk =====
                    var parentChunk = new ParentChunk
                    {
                        DocumentId = document.Id,
                        Content = text,
                        PageNumber = pageNumber
                    };

                    _db.ParentChunks.Add(parentChunk);
                    await _db.SaveChangesAsync();

                    _logger.LogInformation(
                        "Đã lưu ParentChunk: {ParentId}",
                        parentChunk.Id);

                    // ===== Child Chunk =====
                    var childTexts = SplitIntoChildChunks(text, 200);

                    _logger.LogInformation(
                        "Số ChildChunk của trang {Page}: {Count}",
                        pageNumber,
                        childTexts.Count);

                    foreach (var childText in childTexts)
                    {
                        _logger.LogInformation(
                            "Đang gọi Gemini ({Words} từ)...",
                            childText.Split(' ').Length);

                        float[] embedding =
                            await _geminiService.GetEmbeddingAsync(childText);

                        _logger.LogInformation(
                            "Embedding nhận được: {Length}",
                            embedding.Length);

                        var childChunk = new ChildChunk
                        {
                            ParentChunkId = parentChunk.Id,
                            Content = childText,
                            Embedding = new Pgvector.Vector(embedding)
                        };

                        _db.ChildChunks.Add(childChunk);
                    }

                    await _db.SaveChangesAsync();

                    _logger.LogInformation(
                        "Đã lưu xong ChildChunk của trang {Page}",
                        pageNumber);

                    pageNumber++;
                }

                document.Status = "Completed";
                await _db.SaveChangesAsync();

                _logger.LogInformation("========== HOÀN THÀNH XỬ LÝ & VECTORIZE ==========");
            }
            catch (Exception ex)
            {
                document.Status = "Failed";
                await _db.SaveChangesAsync();

                _logger.LogError(ex, "LỖI CHI TIẾT: {Message}", ex.Message);

                throw;
            }
        }

        private string ExtractSampleTextForModeration(string filePath, int maxPages)
        {
            try
            {
                using var pdf = PdfDocument.Open(filePath);
                var sb = new StringBuilder();
                int pageCount = 0;
                foreach (var page in pdf.GetPages())
                {
                    sb.AppendLine(page.Text);
                    pageCount++;
                    if (pageCount >= maxPages) break;
                }
                var text = sb.ToString();
                return text.Length > 8000 ? text.Substring(0, 8000) : text;
            }
            catch
            {
                return string.Empty;
            }
        }

        private List<string> SplitIntoChildChunks(string text, int chunkSize)
        {
            var words = text.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            var chunks = new List<string>();

            for (int i = 0; i < words.Length; i += chunkSize)
            {
                var chunk = string.Join(" ", words.Skip(i).Take(chunkSize));
                chunks.Add(chunk);
            }

            return chunks;
        }
    }
}