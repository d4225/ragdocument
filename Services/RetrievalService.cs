using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;

namespace SmartDocumentRAG.API.Services;

public class RetrievalService : IRetrievalService
{
    private readonly ApplicationDbContext _db;
    private readonly IGeminiService _geminiService;

    public RetrievalService(ApplicationDbContext db, IGeminiService geminiService)
    {
        _db = db;
        _geminiService = geminiService;
    }

    public async Task<List<SearchResultDto>> HybridSearchAsync(string query, int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<SearchResultDto>();

        // 1. Vector Search: Sinh embedding cho câu query của user
        float[] queryEmbedding = await _geminiService.GetEmbeddingAsync(query);
        var vectorQuery = new Pgvector.Vector(queryEmbedding);

        // Lấy Top 20 theo Cosine Distance (vector_cosine_ops)
        var vectorResults = await _db.ChildChunks
            .Select(c => new
            {
                c.Id,
                c.ParentChunkId,
                c.Content,
                Distance = c.Embedding.CosineDistance(vectorQuery)
            })
            .OrderBy(c => c.Distance)
            .Take(20)
            .ToListAsync();

        // 2. Keyword Search: Full-Text Search trên PostgreSQL
        // Dùng EF.Functions.ToTsQuery hoặc EF.Functions.WebSearchToTsQuery
        var keywordResults = await _db.ChildChunks
            .Where(c => c.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", query)))
            .Take(20)
            .Select(c => new
            {
                c.Id,
                c.ParentChunkId,
                c.Content
            })
            .ToListAsync();

        // 3. Kết hợp kết quả bằng Reciprocal Rank Fusion (RRF)
        var scoreDict = new Dictionary<Guid, double>();
        var childChunkMap = new Dictionary<Guid, (Guid ParentId, string Content)>();

        int k = 60; // Hằng số RRF chuẩn

        // Tính rank cho Vector Search
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var item = vectorResults[i];
            double rrfScore = 1.0 / (k + (i + 1));

            scoreDict[item.Id] = scoreDict.GetValueOrDefault(item.Id, 0) + rrfScore;
            childChunkMap[item.Id] = (item.ParentChunkId, item.Content);
        }

        // Tính rank cho Keyword Search
        for (int i = 0; i < keywordResults.Count; i++)
        {
            var item = keywordResults[i];
            double rrfScore = 1.0 / (k + (i + 1));

            scoreDict[item.Id] = scoreDict.GetValueOrDefault(item.Id, 0) + rrfScore;
            childChunkMap[item.Id] = (item.ParentChunkId, item.Content);
        }

        // Lấy Top K ChildChunk có RRF Score cao nhất
        var topChildIds = scoreDict
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => kv.Key)
            .ToList();

        // 4. Cơ chế Parent Retrieval: Từ Top Child Chunks -> Lấy ParentChunk đầy đủ
        var parentIds = topChildIds
            .Select(id => childChunkMap[id].ParentId)
            .Distinct()
            .ToList();

        var parentChunks = await _db.ParentChunks
            .Where(p => parentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        // 5. Đóng gói kết quả DTO
        var finalResults = new List<SearchResultDto>();
        foreach (var childId in topChildIds)
        {
            var (parentId, childContent) = childChunkMap[childId];
            if (parentChunks.TryGetValue(parentId, out var parentChunk))
            {
                finalResults.Add(new SearchResultDto
                {
                    ChildChunkId = childId,
                    ParentChunkId = parentId,
                    ChildContent = childContent,
                    ParentContent = parentChunk.Content, // Trả về nội dung lớn (Parent)
                    PageNumber = parentChunk.PageNumber,
                    Score = scoreDict[childId]
                });
            }
        }

        return finalResults;
    }
}