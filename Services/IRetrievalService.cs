using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;

namespace SmartDocumentRAG.API.Services;

public interface IRetrievalService
{
    // Thêm tham số documentIds vào đây
    Task<List<ParentChunk>> GetRelevantParentChunksAsync(string query, int topK = 5, List<Guid>? documentIds = null);
    Task<List<SearchResultDto>> HybridSearchAsync(string query, int topK = 5, List<Guid>? documentIds = null);
}
