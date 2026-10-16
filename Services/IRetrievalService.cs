using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;

namespace SmartDocumentRAG.API.Services;

public interface IRetrievalService
{
    Task<List<SearchResultDto>> HybridSearchAsync(string query, int topK = 5);
    Task<List<ParentChunk>> GetRelevantParentChunksAsync(string query, int topK = 4);
}
