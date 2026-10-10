using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SmartDocumentRAG.API.Services;

public class AIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AIService> _logger;

    public AIService(HttpClient httpClient, IConfiguration config, ILogger<AIService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var provider = _config["AISettings:Provider"] ?? "Gemini";

        return provider.ToLowerInvariant() switch
        {
            "ollama" => await GetOllamaEmbeddingAsync(text),
            "openai" => await GetOpenAIEmbeddingAsync(text),
            _ => await GetGeminiEmbeddingAsync(text)
        };
    }

    public async Task<string> GenerateAnswerAsync(string prompt)
    {
        var provider = _config["AISettings:Provider"] ?? "Gemini";

        return provider.ToLowerInvariant() switch
        {
            "ollama" => await GenerateOllamaAnswerAsync(prompt),
            "openai" => await GenerateOpenAIAnswerAsync(prompt),
            _ => await GenerateGeminiAnswerAsync(prompt)
        };
    }

    #region Gemini Implementation
    private async Task<float[]> GetGeminiEmbeddingAsync(string text)
    {
        var apiKey = _config["GeminiSettings:ApiKey"]
            ?? _config["AISettings:ApiKey"]
            ?? throw new InvalidOperationException("Thiếu ApiKey cho Gemini trong appsettings.json (GeminiSettings:ApiKey).");

        var model = _config["GeminiSettings:EmbeddingModel"] ?? "text-embedding-004";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:embedContent?key={apiKey}";

        var requestBody = new
        {
            model = $"models/{model}",
            content = new
            {
                parts = new[] { new { text = text } }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Gemini Embedding thất bại ({response.StatusCode}): {err}");
        }

        var resJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resJson);
        var values = doc.RootElement.GetProperty("embedding").GetProperty("values");

        var list = new List<float>();
        foreach (var v in values.EnumerateArray())
        {
            list.Add(v.GetSingle());
        }

        return list.ToArray();
    }

    private async Task<string> GenerateGeminiAnswerAsync(string prompt)
    {
        var apiKey = _config["GeminiSettings:ApiKey"]
            ?? _config["AISettings:ApiKey"]
            ?? throw new InvalidOperationException("Thiếu ApiKey cho Gemini trong appsettings.json (GeminiSettings:ApiKey).");

        var model = _config["GeminiSettings:ChatModel"] ?? "gemini-1.5-flash";
        model = model.Replace("models/", "").Trim();

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

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

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Gemini Chat thất bại ({response.StatusCode}): {err}");
        }

        var resJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resJson);

        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var text = candidates[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text ?? "Không nhận được phản hồi từ AI.";
        }

        return "AI không trả về nội dung trả lời.";
    }
    #endregion

    #region Ollama Implementation
    private async Task<float[]> GetOllamaEmbeddingAsync(string text)
    {
        var endpoint = _config["OllamaSettings:Endpoint"] ?? "http://localhost:11434";
        var model = _config["OllamaSettings:EmbeddingModel"] ?? "nomic-embed-text";

        var url = $"{endpoint.TrimEnd('/')}/api/embeddings";
        var requestBody = new { model = model, prompt = text };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Ollama Embedding thất bại ({response.StatusCode}): {err}");
        }

        var resJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resJson);
        var embeddingArray = doc.RootElement.GetProperty("embedding");

        var list = new List<float>();
        foreach (var v in embeddingArray.EnumerateArray())
        {
            list.Add(v.GetSingle());
        }

        return list.ToArray();
    }

    private async Task<string> GenerateOllamaAnswerAsync(string prompt)
    {
        var endpoint = _config["OllamaSettings:Endpoint"] ?? "http://localhost:11434";
        var model = _config["OllamaSettings:ChatModel"] ?? "llama3";

        var url = $"{endpoint.TrimEnd('/')}/api/generate";
        var requestBody = new { model = model, prompt = prompt, stream = false };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Ollama Chat thất bại ({response.StatusCode}): {err}");
        }

        var resJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resJson);
        return doc.RootElement.GetProperty("response").GetString() ?? "Không nhận được phản hồi từ Ollama.";
    }
    #endregion

    #region OpenAI Implementation
    private async Task<float[]> GetOpenAIEmbeddingAsync(string text)
    {
        var apiKey = _config["OpenAISettings:ApiKey"] ?? throw new InvalidOperationException("Thiếu OpenAISettings:ApiKey");
        var endpoint = _config["OpenAISettings:Endpoint"] ?? "https://api.openai.com/v1";
        var model = _config["OpenAISettings:EmbeddingModel"] ?? "text-embedding-3-small";

        var url = $"{endpoint.TrimEnd('/')}/embeddings";
        var requestBody = new
        {
            model = model,
            input = text,
            dimensions = 768
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"OpenAI Embedding thất bại ({response.StatusCode}): {err}");
        }

        var resJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resJson);
        var data = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

        var list = new List<float>();
        foreach (var v in data.EnumerateArray())
        {
            list.Add(v.GetSingle());
        }

        return list.ToArray();
    }

    private async Task<string> GenerateOpenAIAnswerAsync(string prompt)
    {
        var apiKey = _config["OpenAISettings:ApiKey"] ?? throw new InvalidOperationException("Thiếu OpenAISettings:ApiKey");
        var endpoint = _config["OpenAISettings:Endpoint"] ?? "https://api.openai.com/v1";
        var model = _config["OpenAISettings:ChatModel"] ?? "gpt-4o-mini";

        var url = $"{endpoint.TrimEnd('/')}/chat/completions";
        var requestBody = new
        {
            model = model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.2
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"OpenAI Chat thất bại ({response.StatusCode}): {err}");
        }

        var resJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resJson);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
    #endregion
}
