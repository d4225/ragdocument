using Hangfire;
using Microsoft.AspNetCore.Mvc;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;
using SmartDocumentRAG.API.Services;

namespace SmartDocumentRAG.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IWebHostEnvironment _env;

    public DocumentController(
        ApplicationDbContext db,
        IBackgroundJobClient backgroundJobs,
        IWebHostEnvironment env)
    {
        _db = db;
        _backgroundJobs = backgroundJobs;
        _env = env;
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
    [FromServices] IRetrievalService retrievalService,
    [FromQuery] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Vui lòng nhập từ khóa tìm kiếm.");

        var results = await retrievalService.HybridSearchAsync(query, topK);
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
}