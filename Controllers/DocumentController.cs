using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;
using SmartDocumentRAG.API.Services;
using System.Text;
using System.Text.Json;

namespace SmartDocumentRAG.API.Controllers
{
    public class DocumentController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IBackgroundJobClient _backgroundJobs;
        private readonly IWebHostEnvironment _env;
        private readonly IRetrievalService _retrievalService;
        private readonly IGeminiService _geminiService;

        public DocumentController(
            ApplicationDbContext db,
            IBackgroundJobClient backgroundJobs,
            IWebHostEnvironment env,
            IRetrievalService retrievalService,
            IGeminiService geminiService)
        {
            _db = db;
            _backgroundJobs = backgroundJobs;
            _env = env;
            _retrievalService = retrievalService;
            _geminiService = geminiService;
        }

        // =========================================================================
        // PHẦN 1: MVC VIEWS (Trang giao diện Razor - Multi-Page Application)
        // =========================================================================

        // Trang chủ hệ thống (Kho tài liệu chung & Tìm kiếm)
        [HttpGet("/Document")]
        [HttpGet("Document/Index")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Index(string searchString)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            var query = _db.Documents
                .Include(d => d.User)
                .OrderByDescending(d => d.UploadedAt)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(d => d.FileName.Contains(searchString) ||
                                         (d.User != null && d.User.Username.Contains(searchString)));
                ViewData["SearchString"] = searchString;
            }

            var docs = await query.ToListAsync();
            return View(docs);
        }

        // Danh sách tài liệu cá nhân của người dùng đang đăng nhập
        [HttpGet("/Document/List")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> List()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            Guid userId = Guid.Parse(userIdString);

            var docs = await _db.Documents
                .Include(d => d.User)
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync();

            return View(docs);
        }

        // Trang Chi tiết & Hỏi đáp riêng cho 1 tài liệu (URL: /Document/Details/{id})
        [HttpGet("Document/Details/{id:guid}")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Details(Guid id)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            var doc = await _db.Documents
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null) return NotFound();

            return View(doc);
        }

        // Trang giao diện làm Quiz ôn thi riêng biệt (URL: /Document/Quiz/{id})
        [HttpGet("Document/Quiz/{id:guid}")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Quiz(Guid id)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            var doc = await _db.Documents.FindAsync(id);
            if (doc == null) return NotFound();

            return View(doc); // Trỏ tới Views/Document/Quiz.cshtml
        }

        // Trang giao diện Tóm tắt văn bản riêng biệt cho 1 tài liệu (URL: /Document/Summary/{id})
        [HttpGet("Document/Summary/{id:guid}")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Summary(Guid id)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            var doc = await _db.Documents.FindAsync(id);
            if (doc == null) return NotFound();

            return View(doc); // Trỏ tới Views/Document/Summary.cshtml
        }

        // Trang Giao diện Upload riêng biệt (URL: /Document/UploadPage)
        [HttpGet("Document/UploadPage")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult UploadPage()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            return View("Upload");
        }

        // Trang Giao diện Tìm kiếm riêng biệt (URL: /Document/SearchPage)
        [HttpGet("Document/SearchPage")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult SearchPage()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }


        // =========================================================================
        // PHẦN 2: API ENDPOINTS (Xử lý ngầm, AJAX, Web API & Gợi ý Live Search)
        // =========================================================================

        // API: Lấy file PDF để xem trước trực tuyến (Online PDF Preview)
        [HttpGet("api/Document/view/{id:guid}")]
        public async Task<IActionResult> ViewPdf(Guid id)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return Unauthorized();
            }

            var doc = await _db.Documents.FindAsync(id);
            if (doc == null || !System.IO.File.Exists(doc.FilePath))
            {
                return NotFound("Không tìm thấy file tài liệu.");
            }

            var fileStream = new FileStream(doc.FilePath, FileMode.Open, FileAccess.Read);
            return File(fileStream, "application/pdf");
        }

        // API: Lấy danh sách gợi ý tìm kiếm theo thời gian thực (Autocomplete / Live Search)
        [HttpGet("api/Document/suggestions")]
        public async Task<IActionResult> GetSearchSuggestions([FromQuery] string query)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(new List<object>());
            }

            query = query.Trim().ToLower();

            var suggestions = await _db.Documents
                .Where(d => d.FileName.ToLower().Contains(query))
                .OrderByDescending(d => d.UploadedAt)
                .Take(5)
                .Select(d => new { id = d.Id, fileName = d.FileName })
                .ToListAsync();

            return Ok(suggestions);
        }

        // POST: /api/Document/upload
        [HttpPost("api/Document/upload")]
        public async Task<IActionResult> UploadDocument(IFormFile file)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return Unauthorized("Vui lòng đăng nhập để thực hiện chức năng này.");
            }

            Guid userId = Guid.Parse(userIdString);

            if (file == null || file.Length == 0)
            {
                return BadRequest("Vui lòng chọn file PDF.");
            }

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Chỉ chấp nhận file PDF.");
            }

            var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var savedFileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsFolder, savedFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

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

            // Đưa vào Hangfire background job để bóc tách PDF & vectorize
            _backgroundJobs.Enqueue<DocumentProcessingJob>(
                job => job.ProcessPdfAsync(document.Id));

            return Accepted(new
            {
                message = "Upload thành công",
                documentId = document.Id,
                status = document.Status
            });
        }

        // GET: /api/Document/search
        [HttpGet("api/Document/search")]
        public async Task<IActionResult> Search(
            [FromQuery] string query,
            [FromQuery] int topK = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Vui lòng nhập từ khóa tìm kiếm.");

            var results = await _retrievalService.HybridSearchAsync(query, topK);
            return Ok(results);
        }

        // POST: /api/Document/chat
        [HttpPost("api/Document/chat")]
        public async Task<IActionResult> Chat(
            [FromBody] ChatRequestDto request,
            [FromServices] IChatRAGService chatRagService)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Câu hỏi không được để trống.");

            var response = await chatRagService.AskQuestionAsync(request);
            return Ok(response);
        }

        // GET: /api/Document/sessions
        [HttpGet("api/Document/sessions")]
        public async Task<IActionResult> GetSessions([FromServices] ApplicationDbContext db)
        {
            var sessions = await db.ChatSessions
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.Id, s.Title, s.CreatedAt })
                .ToListAsync();

            return Ok(sessions);
        }

        // GET: /api/Document/sessions/{sessionId}/messages
        [HttpGet("api/Document/sessions/{sessionId:guid}/messages")]
        public async Task<IActionResult> GetSessionMessages(Guid sessionId, [FromServices] ApplicationDbContext db)
        {
            var messages = await db.ChatMessages
                .Where(m => m.ChatSessionId == sessionId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            return Ok(messages);
        }

        // POST: /api/Document/chat-stream (Server-Sent Events Realtime Streaming)
        [HttpPost("api/Document/chat-stream")]
        public async Task ChatStream([FromBody] ChatRequestDto request, CancellationToken cancellationToken)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

            try
            {
                var topChunks = await _retrievalService.GetRelevantParentChunksAsync(
                    request.Question,
                    topK: request.TopK,
                    documentIds: request.DocumentIds
                );

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

                await foreach (var chunk in _geminiService.StreamAnswerAsync(systemPrompt).WithCancellation(cancellationToken))
                {
                    var data = JsonSerializer.Serialize(new { text = chunk });
                    await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

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

        // POST: /api/Document/generate-quiz
        [HttpPost("api/Document/generate-quiz")]
        public async Task<IActionResult> GenerateQuiz([FromBody] QuizRequestDto request)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString)) return Unauthorized();

            var doc = await _db.Documents.FindAsync(request.DocumentId);
            if (doc == null) return NotFound("Không tìm thấy tài liệu.");

            var chunks = await _retrievalService.GetRelevantParentChunksAsync(
                "Tóm tắt nội dung chính, định nghĩa và kiến thức quan trọng",
                topK: 10,
                documentIds: new List<Guid> { request.DocumentId }
            );

            var contextBuilder = new StringBuilder();
            foreach (var c in chunks)
            {
                contextBuilder.AppendLine(c.Content);
            }

            var quizQuestions = await _geminiService.GenerateQuizAsync(contextBuilder.ToString(), request.QuestionCount);

            if (quizQuestions == null || !quizQuestions.Any())
            {
                return BadRequest("Không thể tạo quiz từ tài liệu này. Vui lòng thử lại.");
            }

            return Ok(quizQuestions);
        }

        // DTO nhận yêu cầu tóm tắt văn bản
        public class SummaryRequestDto
        {
            public Guid DocumentId { get; set; }
            public string SummaryStyle { get; set; } = "bullets";
        }

        // POST: /api/Document/summarize
        [HttpPost("api/Document/summarize")]
        public async Task<IActionResult> Summarize([FromBody] SummaryRequestDto request)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString)) return Unauthorized();

            var doc = await _db.Documents.FindAsync(request.DocumentId);
            if (doc == null) return NotFound("Không tìm thấy tài liệu.");

            var chunks = await _retrievalService.GetRelevantParentChunksAsync(
                "Tóm tắt toàn bộ nội dung chính, luận điểm và kết luận",
                topK: 12,
                documentIds: new List<Guid> { request.DocumentId }
            );

            var contextBuilder = new StringBuilder();
            foreach (var c in chunks)
            {
                contextBuilder.AppendLine(c.Content);
            }

            string styleInstruction = request.SummaryStyle switch
            {
                "bullets" => "Trình bày kết quả tóm tắt dưới dạng các gạch đầu dòng (bullet points) rõ ràng, súc tích.",
                "keypoints" => "Liệt kê các ý chính cốt lõi nhất (Key takeaways) và từ khóa quan trọng.",
                "paragraph" => "Trình bày kết quả dưới dạng các đoạn văn xuôi mạch lạc, mang văn phong học thuật.",
                _ => "Tóm tắt ngắn gọn."
            };

            var prompt = $"""
            Bạn là một trợ lý AI phân tích tài liệu học tập chuyên nghiệp. 
            Yêu cầu: {styleInstruction}
            Giữ lại đầy đủ các thông tin cốt lõi, không bịa đặt thêm thông tin ngoài văn bản gốc.

            VĂN BẢN TÀI LIỆU CẦN TÓM TẮT:
            {contextBuilder}
            """;

            var summaryResult = await _geminiService.GenerateAnswerAsync(prompt);
            return Ok(new { summary = summaryResult });
        }
    }
}