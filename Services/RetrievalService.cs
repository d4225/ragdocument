using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;

namespace SmartDocumentRAG.API.Services;

public class RetrievalService : IRetrievalService
{
    private readonly ApplicationDbContext _db;
    private readonly IAIService _aiService;
    private readonly ILogger<RetrievalService> _logger;

    public RetrievalService(
        ApplicationDbContext db,
        IAIService aiService,
        ILogger<RetrievalService> logger)
    {
        _db = db;
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<List<SearchResultDto>> HybridSearchAsync(string query, int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResultDto>();

        // 1. Tạo Vector Embedding (768 chiều) cho câu hỏi của người dùng
        float[] queryEmbedding = await _aiService.GetEmbeddingAsync(query);
        var queryVector = new Vector(queryEmbedding);

        // 2. Vector Cosine Distance Search trên ChildChunks
        // Lấy top 10 child chunks có khoảng cách cosine nhỏ nhất
        var childHits = await _db.ChildChunks
            .Where(c => c.Embedding != null)
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(Math.Max(topK * 2, 10))
            .Select(c => new
            {
                c.Id,
                c.ParentChunkId,
                c.Content,
                Distance = c.Embedding!.CosineDistance(queryVector)
            })
            .ToListAsync();

        if (childHits.Count == 0)
        {
            _logger.LogInformation("Không tìm thấy ChildChunk phù hợp cho câu hỏi.");
            return new List<SearchResultDto>();
        }

        // 3. Parent Retrieval: Thu thập ParentChunkId duy nhất
        var parentChunkIds = childHits
            .Select(c => c.ParentChunkId)
            .Distinct()
            .Take(topK)
            .ToList();

        var parentMap = await _db.ParentChunks
            .Include(p => p.Document)
            .Where(p => parentChunkIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // 4. Kết hợp kết quả
        var results = new List<SearchResultDto>();
        var seenParents = new HashSet<Guid>();

        foreach (var hit in childHits)
        {
            if (seenParents.Contains(hit.ParentChunkId))
                continue;

            if (parentMap.TryGetValue(hit.ParentChunkId, out var parentChunk))
            {
                seenParents.Add(hit.ParentChunkId);
                results.Add(new SearchResultDto
                {
                    ChildChunkId = hit.Id,
                    ParentChunkId = parentChunk.Id,
                    DocumentId = parentChunk.DocumentId,
                    DocumentName = parentChunk.Document?.FileName ?? "Tài liệu",
                    ChildContent = hit.Content,
                    ParentContent = parentChunk.Content,
                    PageNumber = parentChunk.PageNumber,
                    Distance = hit.Distance
                });
            }

            if (results.Count >= topK)
                break;
        }

        return results;
    }
}