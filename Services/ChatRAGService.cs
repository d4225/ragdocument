using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;

namespace SmartDocumentRAG.API.Services;

public class ChatRAGService : IChatRAGService
{
    private readonly IRetrievalService _retrievalService;
    private readonly IGeminiService _geminiService;
    private readonly ApplicationDbContext _db;

    public ChatRAGService(
        IRetrievalService retrievalService,
        IGeminiService geminiService,
        ApplicationDbContext db)
    {
        _retrievalService = retrievalService;
        _geminiService = geminiService;
        _db = db;
    }

    public async Task<ChatResponseDto> AskQuestionAsync(ChatRequestDto request)
    {
        // 1. Hybrid Search
        var searchResults = await _retrievalService
            .HybridSearchAsync(request.Question, request.TopK);

        if (!searchResults.Any())
        {
            return new ChatResponseDto
            {
                Answer = "Tôi không tìm thấy thông tin phù hợp trong tài liệu để trả lời câu hỏi của bạn.",
                Citations = new List<CitationDto>()
            };
        }

        // 2. Lấy danh sách ParentChunk duy nhất
        var parentChunkIds = searchResults
            .Select(r => r.ParentChunkId)
            .Distinct()
            .ToList();

        var parentDetails = await _db.ParentChunks
            .Include(p => p.Document)
            .Where(p => parentChunkIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // 3. Xây dựng Context + Citations
        var contextBuilder = new StringBuilder();
        var citations = new List<CitationDto>();

        int index = 1;

        // Chỉ xử lý mỗi ParentChunk một lần
        foreach (var result in searchResults
            .GroupBy(r => r.ParentChunkId)
            .Select(g => g.First()))
        {
            if (!parentDetails.TryGetValue(
                result.ParentChunkId,
                out var parentChunk))
            {
                continue;
            }

            string docName =
                parentChunk.Document?.FileName
                ?? "Tài liệu không xác định";

            Guid docId = parentChunk.DocumentId;

            // Thêm Context
            contextBuilder.AppendLine(
                $"--- NGUỒN TRÍCH DẪN [{index}] ---");

            contextBuilder.AppendLine(
                $"Tên tài liệu: {docName}");

            contextBuilder.AppendLine(
                $"Trang: {parentChunk.PageNumber}");

            contextBuilder.AppendLine(
                $"Nội dung: {parentChunk.Content}");

            contextBuilder.AppendLine();

            // Citation
            citations.Add(new CitationDto
            {
                DocumentId = docId,
                DocumentName = docName,
                PageNumber = parentChunk.PageNumber,
                Snippet = parentChunk.Content.Length > 150
                    ? parentChunk.Content.Substring(0, 150) + "..."
                    : parentChunk.Content
            });

            index++;
        }

        // 4. Xây dựng Prompt cho Gemini
        string systemPrompt = $@"
Bạn là trợ lý hỏi đáp tài liệu.

Hãy trả lời câu hỏi của người dùng dựa CHỈ vào thông tin trong CONTEXT.

YÊU CẦU:
- Chỉ sử dụng thông tin có trong CONTEXT.
- Không tự suy đoán hoặc thêm thông tin không có trong CONTEXT.
- Trả lời ngắn gọn, chính xác và dễ hiểu.
- Nếu CONTEXT không chứa thông tin để trả lời, hãy trả lời đúng câu:
  ""Tài liệu không đề cập"".
- Không cần nhắc lại toàn bộ CONTEXT.
- Không tạo nguồn trích dẫn mới ngoài các nguồn được cung cấp.

CONTEXT:
{contextBuilder}

CÂU HỎI:
{request.Question}

CÂU TRẢ LỜI:
";

        // 5. Gọi Gemini
        string aiAnswer =
            await _geminiService.GenerateAnswerAsync(systemPrompt);

        // 6. Trả kết quả
        return new ChatResponseDto
        {
            Answer = aiAnswer,
            Citations = citations
        };
    }
}