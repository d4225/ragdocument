using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;
using SmartDocumentRAG.API.Services;

namespace SmartDocumentRAG.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        ApplicationDbContext db,
        IBackgroundJobClient backgroundJobs,
        IWebHostEnvironment env,
        ILogger<DocumentsController> logger)
    {
        _db = db;
        _backgroundJobs = backgroundJobs;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách tất cả tài liệu kèm trạng thái
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> GetAllDocuments()
    {
        var docs = await _db.Documents
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentDto
            {
                Id = d.Id,
                FileName = d.FileName,
                FileSize = d.FileSize,
                Status = d.Status.ToString(),
                ErrorMessage = d.ErrorMessage,
                UploadedAt = d.UploadedAt,
                TotalPages = d.ParentChunks.Select(p => p.PageNumber).Distinct().Count()
            })
            .ToListAsync();

        return Ok(docs);
    }

    /// <summary>
    /// Lấy chi tiết một tài liệu
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> GetDocumentById(Guid id)
    {
        var doc = await _db.Documents
            .Where(d => d.Id == id)
            .Select(d => new DocumentDto
            {
                Id = d.Id,
                FileName = d.FileName,
                FileSize = d.FileSize,
                Status = d.Status.ToString(),
                ErrorMessage = d.ErrorMessage,
                UploadedAt = d.UploadedAt,
                TotalPages = d.ParentChunks.Select(p => p.PageNumber).Distinct().Count()
            })
            .FirstOrDefaultAsync();

        if (doc == null)
            return NotFound(new { message = "Không tìm thấy tài liệu." });

        return Ok(doc);
    }

    /// <summary>
    /// Upload tài liệu PDF và đưa vào Hangfire Background Queue
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadDocument([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Vui lòng chọn file PDF hợp lệ." });
        }

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Hệ thống chỉ chấp nhận file định dạng PDF." });
        }

        var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads");
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var fullPath = Path.Combine(uploadsFolder, uniqueFileName);

        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = file.FileName,
            FilePath = fullPath,
            FileSize = file.Length,
            Status = DocumentStatus.Pending,
            ErrorMessage = null,
            UploadedAt = DateTimeOffset.UtcNow
        };

        _db.Documents.Add(document);
        await _db.SaveChangesAsync();

        // Đưa job xử lý ngầm vào Hangfire
        _backgroundJobs.Enqueue<IDocumentProcessorJob>(job => job.ProcessPdfAsync(document.Id));

        _logger.LogInformation("Đã tiếp nhận file {FileName} (Id: {Id}), trạng thái: Pending", document.FileName, document.Id);

        return Accepted(new
        {
            message = "Tải lên tài liệu thành công. Đang tiến hành xử lý ngầm...",
            documentId = document.Id,
            fileName = document.FileName,
            status = document.Status.ToString()
        });
    }

    /// <summary>
    /// Stream file PDF trực tiếp phục vụ PDF Viewer trên Frontend
    /// </summary>
    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetPdfFile(Guid id)
    {
        var doc = await _db.Documents.FindAsync(id);
        if (doc == null || !System.IO.File.Exists(doc.FilePath))
        {
            return NotFound(new { message = "Không tìm thấy tệp PDF trên máy chủ." });
        }

        var stream = new FileStream(doc.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }

    /// <summary>
    /// Xóa tài liệu và xóa cascading toàn bộ chunks, vector trong DB cũng như file vật lý
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        var doc = await _db.Documents.FindAsync(id);
        if (doc == null)
        {
            return NotFound(new { message = "Không tìm thấy tài liệu để xóa." });
        }

        // Xóa file vật lý trên đĩa
        try
        {
            if (System.IO.File.Exists(doc.FilePath))
            {
                System.IO.File.Delete(doc.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể xóa file vật lý tại {FilePath}", doc.FilePath);
        }

        // Xóa trong DB (sẽ tự động cascade xóa ParentChunks và ChildChunks nhờ cấu hình EF Core)
        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Đã xóa hoàn toàn tài liệu {FileName} (Id: {Id})", doc.FileName, id);

        return Ok(new { message = "Đã xóa tài liệu và các vector liên quan thành công." });
    }
}
