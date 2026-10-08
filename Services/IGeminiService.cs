namespace SmartDocumentRAG.API.Services;

public interface IGeminiService
{
    Task<float[]> GetEmbeddingAsync(string text);
    Task<string> GenerateAnswerAsync(string prompt);
}