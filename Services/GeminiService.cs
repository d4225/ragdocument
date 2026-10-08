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
}