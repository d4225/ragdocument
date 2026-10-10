using SmartDocumentRAG.API.DTOs;

namespace SmartDocumentRAG.API.Services;

public interface IRetrievalService
{
    Task<List<SearchResultDto>> HybridSearchAsync(string query, int topK = 5);
}