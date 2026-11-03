using SmartDocumentRAG.API.DTOs;
using System.Text;
using System.Text.Json;

namespace SmartDocumentRAG.API.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _embeddingModel;
    private readonly string _chatModel;

    public GeminiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        // Đọc cấu hình nhất quán từ GeminiSettings trong appsettings.json
        _apiKey = configuration["GeminiSettings:ApiKey"]
            ?? throw new ArgumentNullException("Thiếu GeminiSettings:ApiKey trong appsettings.json");

        _embeddingModel = configuration["GeminiSettings:EmbeddingModel"] ?? "text-embedding-004";
        _chatModel = configuration["GeminiSettings:ChatModel"] ?? "gemini-3.5-flash";
    }

    /// <summary>
    /// Tạo Vector Embeddings (768 chiều) từ đoạn văn bản (Tuần 3 & 4)
    /// </summary>
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_embeddingModel}:embedContent?key={_apiKey}";

        var requestBody = new
        {
            model = $"models/{_embeddingModel}",
            content = new
            {
                parts = new[]
                {
                    new { text = text }
                }
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(url, jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new Exception($"Lỗi khi gọi Gemini Embedding API ({response.StatusCode}): {errorText}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        // Đọc mảng float 768 chiều từ JSON response
        var values = doc.RootElement
            .GetProperty("embedding")
            .GetProperty("values");

        var embedding = new List<float>();
        foreach (var val in values.EnumerateArray())
        {
            embedding.Add(val.GetSingle());
        }

        return embedding.ToArray();
    }

    /// <summary>
    /// Gọi Gemini LLM để sinh câu trả lời dựa trên Prompt kèm Context (Tuần 5)
    /// </summary>
    public async Task<string> GenerateAnswerAsync(string prompt)
{
    var rawModel = string.IsNullOrWhiteSpace(_chatModel) ? "gemini-3.8-flash" : _chatModel;
    rawModel = rawModel.Replace("models/", "").Trim();

    var url = $"https://generativelanguage.googleapis.com/v1beta/models/{rawModel}:generateContent?key={_apiKey}";

    var requestBody = new
    {
        contents = new[]
        {
            new
            {
                parts = new[]
                {
                    new { text = prompt }
                }
            }
        },
        generationConfig = new
        {
            temperature = 0.2,
            maxOutputTokens = 2048
        }
    };

    var jsonContent = new StringContent(
        JsonSerializer.Serialize(requestBody),
        Encoding.UTF8,
        "application/json"
    );

    try
    {
        // Request sẽ sử dụng Timeout 3 phút đã cấu hình ở Program.cs
        var response = await _httpClient.PostAsync(url, jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new Exception($"Lỗi khi gọi Gemini Chat API ({response.StatusCode}): {errorText}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        var candidates = doc.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() > 0)
        {
            var text = candidates[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text ?? "Không nhận được phản hồi nội dung từ Gemini.";
        }

        return "Gemini không thể khởi tạo câu trả lời.";
    }
    catch (TaskCanceledException)
    {
        throw new Exception("Yêu cầu bị quá thời gian chờ (Timeout). Vui lòng thử lại với câu hỏi ngắn hơn.");
    }
}
    public async IAsyncEnumerable<string> StreamAnswerAsync(string prompt)
    {
        var rawModel = string.IsNullOrWhiteSpace(_chatModel) ? "gemini-3.5-flash" : _chatModel;
        rawModel = rawModel.Replace("models/", "").Trim();

        // Dùng endpoint streamGenerateContent thay vì generateContent
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{rawModel}:streamGenerateContent?alt=sse&key={_apiKey}";

        var requestBody = new
        {
            contents = new[]
            {
            new { parts = new[] { new { text = prompt } } }
        },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 2048
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = jsonContent };
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new Exception($"Lỗi Gemini Stream API ({response.StatusCode}): {errorText}");
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Dòng SSE từ Gemini bắt đầu bằng "data: "
            if (line.StartsWith("data: "))
            {
                var jsonStr = line.Substring(6).Trim();

                using var doc = JsonDocument.Parse(jsonStr);
                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() > 0)
                {
                    var parts = candidates[0].GetProperty("content").GetProperty("parts");
                    if (parts.GetArrayLength() > 0)
                    {
                        var text = parts[0].GetProperty("text").GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return text;
                        }
                    }
                }
            }
        }
    }
    // Trong GeminiService.cs (hoặc dịch vụ xử lý AI hiện tại của bạn)
    public async Task<List<QuizQuestionDto>> GenerateQuizAsync(string documentContext, int questionCount)
    {
        var rawModel = string.IsNullOrWhiteSpace(_chatModel) ? "gemini-1.5-flash" : _chatModel;
        rawModel = rawModel.Replace("models/", "").Trim();

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{rawModel}:generateContent?key={_apiKey}";

        var prompt = $@"
Dựa vào nội dung tài liệu sau, hãy tạo một bộ câu hỏi trắc nghiệm gồm {questionCount} câu hỏi.
Bắt buộc trả về đúng định dạng danh sách JSON (không thêm bất kỳ lời dẫn hay văn bản nào khác):
[
  {{
    ""questionNumber"": 1,
    ""questionText"": ""Nội dung câu hỏi"",
    ""options"": [""A. ..."", ""B. ..."", ""C. ..."", ""D. ...""],
    ""correctAnswer"": ""A"",
    ""explanation"": ""Giải thích vì sao đúng...""
  }}
]

NỘI DUNG TÀI LIỆU:
{documentContext}
";

        var requestBody = new
        {
            contents = new[]
            {
            new { parts = new[] { new { text = prompt } } }
        },
            generationConfig = new
            {
                temperature = 0.2,
                responseMimeType = "application/json", // 🔥 BẮT BUỘC GEMINI TRẢ VỀ JSON THUẦN (KHÔNG CHÈM CHỮ CHÀO)
                maxOutputTokens = 4096                  // 🔥 TĂNG TOKEN ĐỂ KHÔNG BỊ CẮT CỤT DỞ DANG
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(url, jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var errText = await response.Content.ReadAsStringAsync();
            throw new Exception($"Lỗi Gemini API: {errText}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        var rawText = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "[]";

        // Cắt chính xác đoạn JSON nằm giữa [ và ] (chống mọi loại rác/lời chào của AI)
        var startIndex = rawText.IndexOf('[');
        var endIndex = rawText.LastIndexOf(']');

        if (startIndex == -1 || endIndex == -1 || endIndex < startIndex)
        {
            throw new Exception("AI không trả về mảng JSON hợp lệ: " + rawText);
        }

        var cleanJson = rawText.Substring(startIndex, endIndex - startIndex + 1);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<QuizQuestionDto>>(cleanJson, options) ?? new List<QuizQuestionDto>();
    }
    /// <summary>
    /// Kiểm tra nội dung văn bản có an toàn hay không (Chặn đồi trụy, bạo lực, nội dung không phù hợp)
    /// </summary>
    public async Task<(bool IsSafe, string Reason)> EvaluateContentSafetyAsync(string sampleText)
    {
        // Sử dụng $$""" giúp giữ nguyên cặp ngoặc { } của JSON, và dùng {{sampleText}} để chèn biến C#
        var prompt = $$"""
    Bạn là một chuyên gia kiểm duyệt nội dung và an toàn hệ thống học thuật. Hãy phân tích đoạn văn bản mẫu trích từ tài liệu được tải lên dưới đây.
    Nhiệm vụ của bạn là đánh giá xem văn bản này có chứa nội dung đồi trụy, khiêu dâm, bạo lực cực đoan, kích động thù địch hoặc trái pháp luật hay không.

    Trả về duy nhất một JSON object hợp lệ, không có Markdown.

    Quy tắc:
    - isSafe là true nếu nội dung an toàn, false nếu không an toàn.
    - reason phải là chuỗi rỗng "" nếu isSafe = true.
    - Nếu isSafe = false, reason phải nêu lý do cụ thể dựa trên nội dung tài liệu.
    - Không được trả lại nội dung hướng dẫn hoặc văn bản mẫu trong reason.
    - Không sử dụng ký tự NULL (\u0000) trong kết quả.

    Ví dụ cấu trúc:
    {
      "isSafe": false,
      "reason": "Nêu lý do cụ thể tại đây"
    }

    VĂN BẢN CẦN KIỂM TRA:
    {{sampleText}}
    """;

        try
        {
            var rawResponse = await GenerateAnswerAsync(prompt);
            var cleanJson = rawResponse.Replace("```json", "").Replace("```", "").Trim();

            using var doc = JsonDocument.Parse(cleanJson);
            bool isSafe = doc.RootElement.GetProperty("isSafe").GetBoolean();
            string reason = doc.RootElement.GetProperty("reason").GetString() ?? "Vi phạm tiêu chuẩn nội dung.";

            return (isSafe, reason);
        }
        catch
        {
            // Nếu lỗi parse JSON, mặc định cho qua vòng kiểm duyệt cơ bản
            return (true, string.Empty);
        }
    }
}