using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;
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



    public async Task<List<SearchResultDto>> HybridSearchAsync(string query, int topK = 5, List<Guid>? documentIds = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<SearchResultDto>();

        float[] queryEmbedding = await _geminiService.GetEmbeddingAsync(query);
        var vectorQuery = new Pgvector.Vector(queryEmbedding);

        // ==========================================
        // 1. VECTOR SEARCH (Cosine Distance) có lọc DocumentIds
        // ==========================================
        var vectorQueryable = _db.ChildChunks
            .Include(c => c.ParentChunk)
            .ThenInclude(p => p.Document)
            .AsQueryable();

        // Nếu có truyền danh sách file cụ thể, tiến hành lọc
        if (documentIds != null && documentIds.Any())
        {
            vectorQueryable = vectorQueryable.Where(c => documentIds.Contains(c.ParentChunk.DocumentId));
        }

        var vectorResults = await vectorQueryable
            .Select(c => new
            {
                ChildChunk = c,
                Distance = c.Embedding.CosineDistance(vectorQuery)
            })
            .OrderBy(c => c.Distance)
            .Take(20)
            .ToListAsync();

        // ==========================================
        // 2. KEYWORD SEARCH (Full-Text Search) có lọc DocumentIds
        // ==========================================
        var keywordQueryable = _db.ChildChunks
            .Include(c => c.ParentChunk)
            .ThenInclude(p => p.Document)
            .AsQueryable();

        if (documentIds != null && documentIds.Any())
        {
            keywordQueryable = keywordQueryable.Where(c => documentIds.Contains(c.ParentChunk.DocumentId));
        }

        var keywordResults = await keywordQueryable
            .Where(c => c.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", query)))
            .Take(20)
            .Select(c => new
            {
                ChildChunk = c
            })
            .ToListAsync();

        // ==========================================
        // 3. KẾT HỢP BẰNG THUẬT TOÁN RRF (Reciprocal Rank Fusion)
        // ==========================================
        var rrfScores = new Dictionary<Guid, (ChildChunk Chunk, double Score)>();
        const double k = 60.0;

        // Tính điểm RRF cho Vector Search
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var item = vectorResults[i];
            double rrfScore = 1.0 / (k + (i + 1));
            rrfScores[item.ChildChunk.Id] = (item.ChildChunk, rrfScore);
        }

        // Cộng điểm RRF cho Keyword Search
        for (int i = 0; i < keywordResults.Count; i++)
        {
            var item = keywordResults[i];
            double rrfScore = 1.0 / (k + (i + 1));
            if (rrfScores.ContainsKey(item.ChildChunk.Id))
            {
                var existing = rrfScores[item.ChildChunk.Id];
                rrfScores[item.ChildChunk.Id] = (existing.Chunk, existing.Score + rrfScore);
            }
            else
            {
                rrfScores[item.ChildChunk.Id] = (item.ChildChunk, rrfScore);
            }
        }

        // Lấy topK kết quả tốt nhất sau khi gộp Hybrid Search
        var topChildChunks = rrfScores.Values
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();

        // Chuyển đổi sang DTO trả về
        return topChildChunks.Select(c => new SearchResultDto
        {
            ChildChunkId = c.Id,
            ParentChunkId = c.ParentChunkId,
            ChildContent = c.Content,
            ParentContent = c.ParentChunk?.Content,
            PageNumber = c.ParentChunk?.PageNumber ?? 0,
            DocumentName = c.ParentChunk?.Document?.FileName ?? "Tài liệu",
            Score = rrfScores[c.Id].Score
        }).ToList();
    }

    // Cập nhật luôn phương thức lấy Parent Chunks tương tự
    public async Task<List<ParentChunk>> GetRelevantParentChunksAsync(string query, int topK = 5, List<Guid>? documentIds = null)
    {
        var searchResults = await HybridSearchAsync(query, topK, documentIds);
        var parentChunkIds = searchResults.Select(r => r.ParentChunkId).Distinct().ToList();

        var parentChunks = await _db.ParentChunks
            .Include(p => p.Document)
            .Where(p => parentChunkIds.Contains(p.Id))
            .ToListAsync();

        // Sắp xếp lại theo đúng thứ tự điểm RRF
        return parentChunkIds
            .Select(id => parentChunks.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .Cast<ParentChunk>()
            .ToList();
    }

    public async Task<List<ParentChunk>> GetRelevantParentChunksAsync(string query, int topK = 4)

    {

        // Gọi lại Hybrid Search đã viết ở Tuần 4

        var searchResults = await HybridSearchAsync(query, topK);



        var parentChunkIds = searchResults.Select(r => r.ParentChunkId).Distinct().ToList();



        // Truy vấn lấy danh sách ParentChunk kèm thông tin Document

        var parentChunks = await _db.ParentChunks

        .Include(p => p.Document)

        .Where(p => parentChunkIds.Contains(p.Id))

        .ToListAsync();



        return parentChunks;

    }

}