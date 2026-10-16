using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;

namespace SmartDocumentRAG.API.Services;

public interface IChatRAGService
{
    Task<ChatResponseDto> AskQuestionAsync(ChatRequestDto request);
}