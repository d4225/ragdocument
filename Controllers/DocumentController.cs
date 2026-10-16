using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;
using SmartDocumentRAG.API.Services;
using System.Text;
using System.Text.Json; // Bổ sung namespace này để dùng JsonSerializer

namespace SmartDocumentRAG.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IWebHostEnvironment _env;
    private readonly IRetrievalService _retrievalService; // Khai báo service Retrieval
    private readonly IGeminiService _geminiService;         // Khai báo service Gemini

    public DocumentController(
        ApplicationDbContext db,
        IBackgroundJobClient backgroundJobs,
        IWebHostEnvironment env,
        IRetrievalService retrievalService, // Inject vào constructor
        IGeminiService geminiService)         // Inject vào constructor
    {
        _db = db;
        _backgroundJobs = backgroundJobs;
        _env = env;
        _retrievalService = retrievalService;
        _geminiService = geminiService;
    }

    // ==========================
    // MVC: Hiển thị giao diện Razor
    // URL: https://localhost:7155/Document
    // ==========================
    [HttpGet("/Document")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Index()
    {
        return View();
    }

    // ==========================
    // API: Upload PDF
    // POST: /api/Document/upload
    // ==========================
    [HttpPost("upload")]
    public async Task<IActionResult> UploadDocument(
        IFormFile file,
        [FromForm] Guid userId)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Vui lòng chọn file PDF.");
        }

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Chỉ chấp nhận file PDF.");
        }

        // Tạo thư mục Uploads
        var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads");

        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        // Tạo tên file mới
        var savedFileName = $"{Guid.NewGuid()}_{file.FileName}";
        var filePath = Path.Combine(uploadsFolder, savedFileName);

        // Lưu file
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Lưu DB
        var document = new Document
        {
            UserId = userId,
            FileName = file.FileName,
            FilePath = filePath,
            UploadedAt = DateTime.UtcNow,
            Status = "Processing"
        };

        _db.Documents.Add(document);
        await _db.SaveChangesAsync();

        // Đưa vào Hangfire
        _backgroundJobs.Enqueue<DocumentProcessingJob>(
            job => job.ProcessPdfAsync(document.Id));

        return Accepted(new
        {
            message = "Upload thành công",
            documentId = document.Id,
            status = document.Status
        });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string query,
        [FromQuery] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Vui lòng nhập từ khóa tìm kiếm.");

        var results = await _retrievalService.HybridSearchAsync(query, topK);
        return Ok(results);
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat(
        [FromBody] ChatRequestDto request,
        [FromServices] IChatRAGService chatRagService)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("Câu hỏi không được để trống.");

        var response = await chatRagService.AskQuestionAsync(request);
        return Ok(response);
    }

    // GET /api/Document/sessions
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromServices] ApplicationDbContext db)
    {
        var sessions = await db.ChatSessions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Title, s.CreatedAt })
            .ToListAsync();

        return Ok(sessions);
    }

    // GET /api/Document/sessions/{sessionId}/messages
    [HttpGet("sessions/{sessionId:guid}/messages")]
    public async Task<IActionResult> GetSessionMessages(Guid sessionId, [FromServices] ApplicationDbContext db)
    {
        var messages = await db.ChatMessages
            .Where(m => m.ChatSessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return Ok(messages);
    }

    // ==========================
    // API: Streaming Chat (Tuần 7)
    // POST: /api/Document/chat-stream
    // ==========================
    [HttpPost("chat-stream")]
    public async Task ChatStream([FromBody] ChatRequestDto request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            // 1. Lấy context thông qua Retrieval Service
            var topChunks = await _retrievalService.GetRelevantParentChunksAsync(request.Question, topK: request.TopK);

            var contextBuilder = new StringBuilder();
            foreach (var chunk in topChunks)
            {
                contextBuilder.AppendLine($"--- Nguồn: {chunk.Document?.FileName ?? "Tài liệu"} ---");
                contextBuilder.AppendLine(chunk.Content);
                contextBuilder.AppendLine();
            }

            var systemPrompt = $"""
            Bạn là trợ lý AI chuyên phân tích văn bản. Trả lời câu hỏi dựa trên CONTEXT được cung cấp.
            Nếu thông tin không nằm trong Context, hãy lịch sự trả lời "Tài liệu không đề cập".

            CONTEXT TÀI LIỆU:
            {contextBuilder}

            CÂU HỎI:
            {request.Question}
            """;

            // 2. Stream từng đoạn text từ Gemini service trả về client qua SSE
            await foreach (var chunk in _geminiService.StreamAnswerAsync(systemPrompt).WithCancellation(cancellationToken))
            {
                var data = JsonSerializer.Serialize(new { text = chunk });
                await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            // Gửi thông điệp kết thúc kèm Citations
            var citations = topChunks.Select(c => new CitationDto
            {
                DocumentName = c.Document?.FileName ?? "Tài liệu",
                Snippet = c.Content.Length > 150 ? c.Content.Substring(0, 150) + "..." : c.Content
            }).ToList();

            var endData = JsonSerializer.Serialize(new { done = true, citations });
            await Response.WriteAsync($"data: {endData}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var errData = JsonSerializer.Serialize(new { error = ex.Message });
            await Response.WriteAsync($"data: {errData}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}