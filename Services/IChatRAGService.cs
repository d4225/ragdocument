using SmartDocumentRAG.API.DTOs;

namespace SmartDocumentRAG.API.Services;

public interface IChatRAGService
{
    Task<ChatResponseDto> AskQuestionAsync(ChatRequestDto request);
}