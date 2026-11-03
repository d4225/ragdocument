using SmartDocumentRAG.API.DTOs;

namespace SmartDocumentRAG.API.Services;

public interface IGeminiService
{
    Task<float[]> GetEmbeddingAsync(string text);
    Task<string> GenerateAnswerAsync(string prompt);
    IAsyncEnumerable<string> StreamAnswerAsync(string prompt);
    Task<List<QuizQuestionDto>> GenerateQuizAsync(string documentContext, int questionCount);
    Task<(bool IsSafe, string Reason)> EvaluateContentSafetyAsync(string sampleText);
}