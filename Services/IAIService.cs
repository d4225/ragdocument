namespace SmartDocumentRAG.API.Services;

public interface IAIService
{
    Task<float[]> GetEmbeddingAsync(string text);
    Task<string> GenerateAnswerAsync(string prompt);
}
