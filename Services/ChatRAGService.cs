using System.Text;
using System.Text.Json;
using SmartDocumentRAG.API.DTOs;

namespace SmartDocumentRAG.API.Services;

public class ChatRAGService : IChatRAGService
{
    private readonly IRetrievalService _retrievalService;
    private readonly IAIService _aiService;
    private readonly ILogger<ChatRAGService> _logger;

    public ChatRAGService(
        IRetrievalService retrievalService,
        IAIService aiService,
        ILogger<ChatRAGService> logger)
    {
        _retrievalService = retrievalService;
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<ChatResponseDto> AskQuestionAsync(ChatRequestDto request)
    {
        // 1. Hybrid Search + Parent Retrieval
        var searchResults = await _retrievalService.HybridSearchAsync(request.Question, request.TopK);

        if (searchResults.Count == 0)
        {
            return new ChatResponseDto
            {
                Answer = "Tôi không tìm thấy thông tin phù hợp trong các tài liệu hiện có để trả lời câu hỏi của bạn.",
                Citations = new List<CitationDto>()
            };
        }

        // 2. Chuẩn bị danh sách Trích dẫn mặc định từ Search Results (Parent Chunks)
        var defaultCitations = searchResults.Select(r => new CitationDto
        {
            DocumentId = r.DocumentId,
            DocumentName = r.DocumentName,
            PageNumber = r.PageNumber,
            Snippet = r.ParentContent.Length > 200
                ? r.ParentContent.Substring(0, 200) + "..."
                : r.ParentContent
        }).ToList();

        // 3. Xây dựng Context gửi cho LLM
        var contextSb = new StringBuilder();
        int idx = 1;
        foreach (var r in searchResults)
        {
            contextSb.AppendLine($"[NGUỒN {idx}]");
            contextSb.AppendLine($"DocumentId: {r.DocumentId}");
            contextSb.AppendLine($"Tên tài liệu: {r.DocumentName}");
            contextSb.AppendLine($"Trang: {r.PageNumber}");
            contextSb.AppendLine($"Nội dung: {r.ParentContent}");
            contextSb.AppendLine();
            idx++;
        }

        // 4. Prompting ép cấu trúc JSON
        string prompt = $@"
Bạn là trợ lý AI chuyên gia tra cứu tài liệu nội bộ (RAG Assistant).
Dưới đây là các đoạn văn bản trích xuất từ tài liệu (CONTEXT):

{contextSb}

CÂU HỎI CỦA NGƯỜI DÙNG:
{request.Question}

HƯỚNG DẪN BẮT BUỘC:
1. Chỉ dựa trên thông tin trong CONTEXT trên để trả lời câu hỏi. Không suy đoán hay thêm thông tin ngoài tài liệu.
2. Nếu trong CONTEXT không có thông tin để trả lời, trả lời rõ ràng rằng tài liệu không đề cập đến thông tin này.
3. Trả về câu trả lời ở định dạng JSON duy nhất, KHÔNG kèm code markdown hay giải thích ngoài JSON:
{{
  ""answer"": ""Câu trả lời chi tiết và chính xác của bạn bằng tiếng Việt"",
  ""citations"": [
    {{
      ""documentId"": ""Guid của document"",
      ""documentName"": ""Tên tài liệu"",
      ""pageNumber"": 1,
      ""snippet"": ""Đoạn trích ngắn chứng minh""
    }}
  ]
}}
";

        // 5. Gọi AI Service
        string rawResponse = await _aiService.GenerateAnswerAsync(prompt);

        // 6. Xử lý phản hồi JSON
        try
        {
            // Trích xuất JSON nếu model bọc trong ```json ... ```
            string cleanJson = CleanJsonString(rawResponse);
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            string answerText = root.GetProperty("answer").GetString() ?? rawResponse;
            var parsedCitations = new List<CitationDto>();

            if (root.TryGetProperty("citations", out var citationsElem) && citationsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in citationsElem.EnumerateArray())
                {
                    Guid docId = Guid.Empty;
                    if (item.TryGetProperty("documentId", out var idProp))
                    {
                        Guid.TryParse(idProp.GetString(), out docId);
                    }

                    string docName = item.TryGetProperty("documentName", out var nameProp)
                        ? nameProp.GetString() ?? "" : "";

                    int page = item.TryGetProperty("pageNumber", out var pageProp)
                        ? pageProp.GetInt32() : 1;

                    string snippet = item.TryGetProperty("snippet", out var snipProp)
                        ? snipProp.GetString() ?? "" : "";

                    // Nếu docId hoặc docName bị trống, lấy từ defaultCitations tương ứng
                    if (docId == Guid.Empty && defaultCitations.Count > 0)
                    {
                        var match = defaultCitations.FirstOrDefault(d => d.PageNumber == page) ?? defaultCitations[0];
                        docId = match.DocumentId;
                        docName = match.DocumentName;
                    }

                    parsedCitations.Add(new CitationDto
                    {
                        DocumentId = docId,
                        DocumentName = docName,
                        PageNumber = page,
                        Snippet = snippet
                    });
                }
            }

            return new ChatResponseDto
            {
                Answer = answerText,
                Citations = parsedCitations.Count > 0 ? parsedCitations : defaultCitations
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể parse JSON từ AI, sử dụng fallback văn bản thuần.");
            return new ChatResponseDto
            {
                Answer = rawResponse,
                Citations = defaultCitations
            };
        }
    }

    private static string CleanJsonString(string raw)
    {
        string text = raw.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(7);
        }
        else if (text.StartsWith("```"))
        {
            text = text.Substring(3);
        }

        if (text.EndsWith("```"))
        {
            text = text.Substring(0, text.Length - 3);
        }

        return text.Trim();
    }
}