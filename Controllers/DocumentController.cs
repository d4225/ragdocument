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

            if (string.IsNullOrWhiteSpace(searchString))
            {
                query = query.Take(4);
            }
            else
            {
                searchString = searchString.Trim();
                query = query.Where(d => d.FileName.Contains(searchString) ||
                                         (d.User != null && d.User.Username.Contains(searchString)));
                ViewData["SearchString"] = searchString;
            }

            var docs = await query.ToListAsync();
            var userId = Guid.Parse(userIdString);
            ViewBag.FavoriteDocumentIds = await _db.FavoriteDocuments
                .Where(favorite => favorite.UserId == userId)
                .Select(favorite => favorite.DocumentId)
                .ToHashSetAsync();
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

            ViewBag.FavoriteDocumentIds = await _db.FavoriteDocuments
                .Where(favorite => favorite.UserId == userId)
                .Select(favorite => favorite.DocumentId)
                .ToHashSetAsync();

            return View(docs);
        }

        [HttpGet("/Document/Favorites")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> Favorites()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (!Guid.TryParse(userIdString, out var userId))
                return RedirectToAction("Login", "Account");

            var favoriteDocumentIds = await _db.FavoriteDocuments
                .Where(favorite => favorite.UserId == userId)
                .OrderByDescending(favorite => favorite.CreatedAt)
                .Select(favorite => favorite.DocumentId)
                .ToListAsync();

            var documentsById = await _db.Documents
                .Include(document => document.User)
                .Where(document => favoriteDocumentIds.Contains(document.Id))
                .ToDictionaryAsync(document => document.Id);
            var documents = favoriteDocumentIds
                .Where(docId => documentsById.ContainsKey(docId))
                .Select(docId => documentsById[docId])
                .ToList();

            return View(documents);
        }

        [HttpPost("/Document/Favorite/{id:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleFavorite(Guid id, string? returnUrl)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (!Guid.TryParse(userIdString, out var userId))
                return RedirectToAction("Login", "Account");

            var exists = await _db.FavoriteDocuments
                .AnyAsync(favorite => favorite.UserId == userId && favorite.DocumentId == id);
            if (exists)
            {
                var favorite = await _db.FavoriteDocuments.FindAsync(userId, id);
                if (favorite != null) _db.FavoriteDocuments.Remove(favorite);
            }
            else if (await _db.Documents.AnyAsync(document => document.Id == id))
            {
                _db.FavoriteDocuments.Add(new FavoriteDocument { UserId = userId, DocumentId = id });
            }

            await _db.SaveChangesAsync();
            return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction(nameof(Favorites));
        }

        // Cung cấp trạng thái tài liệu của người dùng để trang danh sách tự cập nhật.
        [HttpGet("api/Document/my-statuses")]
        public async Task<IActionResult> GetMyDocumentStatuses(CancellationToken cancellationToken)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (!Guid.TryParse(userIdString, out var userId))
            {
                return Unauthorized();
            }

            var documents = await _db.Documents
                .AsNoTracking()
                .Where(document => document.UserId == userId)
                .Select(document => new
                {
                    documentId = document.Id,
                    status = document.Status,
                    rejectReason = document.RejectReason
                })
                .ToListAsync(cancellationToken);

            return Ok(documents);
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

        [HttpGet("api/Document/{documentId:guid}/chat-history")]
        public async Task<IActionResult> GetDocumentChatHistory(Guid documentId)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (!Guid.TryParse(userIdString, out var userId)) return Unauthorized();

            var ownsDocument = await _db.Documents.AnyAsync(d => d.Id == documentId && d.UserId == userId);
            if (!ownsDocument) return NotFound();

            var session = await _db.ChatSessions
                .Where(s => s.DocumentId == documentId)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new
                {
                    s.Id,
                    Messages = s.Messages.OrderBy(m => m.CreatedAt).Select(m => new
                    {
                        m.Sender,
                        m.Content,
                        m.CreatedAt,
                        m.CitationsJson
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            return Ok(session);
        }

        [HttpPost("api/Document/{documentId:guid}/chat-sessions")]
        public async Task<IActionResult> CreateDocumentChatSession(Guid documentId)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (!Guid.TryParse(userIdString, out var userId)) return Unauthorized();

            var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);
            if (document == null) return NotFound();

            var session = new ChatSession { DocumentId = documentId };
            _db.ChatSessions.Add(session);
            await _db.SaveChangesAsync();
            return Ok(new { sessionId = session.Id });
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
                var userIdString = HttpContext.Session.GetString("UserId");
                if (!Guid.TryParse(userIdString, out var userId))
                {
                    Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var documentId = request.DocumentIds?.SingleOrDefault();
                if (documentId == Guid.Empty || documentId == null ||
                    !await _db.Documents.AnyAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken))
                {
                    Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                ChatSession session;
                if (request.SessionId.HasValue && request.SessionId.Value != Guid.Empty)
                {
                    session = await _db.ChatSessions
                        .Include(s => s.Messages.OrderByDescending(m => m.CreatedAt).Take(6))
                        .FirstOrDefaultAsync(s => s.Id == request.SessionId.Value && s.DocumentId == documentId, cancellationToken)
                        ?? throw new InvalidOperationException("Không tìm thấy phiên trò chuyện của tài liệu.");
                }
                else
                {
                    session = new ChatSession { DocumentId = documentId.Value };
                    _db.ChatSessions.Add(session);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                var recentMessages = session.Messages.OrderBy(m => m.CreatedAt).ToList();
                var topChunks = await _retrievalService.GetRelevantParentChunksAsync(
                    request.Question,
                    topK: request.TopK,
                    documentIds: request.DocumentIds
                );

                var contextBuilder = new StringBuilder();
                var historyBuilder = new StringBuilder();
                foreach (var message in recentMessages)
                {
                    var role = message.Sender == "user" ? "Người dùng" : "Trợ lý AI";
                    historyBuilder.AppendLine($"{role}: {message.Content}");
                }

                foreach (var chunk in topChunks)
                {
                    contextBuilder.AppendLine($"--- Nguồn: {chunk.Document?.FileName ?? "Tài liệu"}, trang {chunk.PageNumber} ---");
                    contextBuilder.AppendLine(chunk.Content);
                    contextBuilder.AppendLine();
                }

                var systemPrompt = $"""
                Bạn là trợ lý AI chuyên phân tích văn bản. Trả lời câu hỏi dựa trên CONTEXT được cung cấp.
                Nếu thông tin không nằm trong Context, hãy lịch sự trả lời "Tài liệu không đề cập".
                Trình bày câu trả lời rõ ràng bằng Markdown đơn giản, không lặp lại dấu định dạng và không tự tạo số trang hoặc trích dẫn.

                LỊCH SỬ TRÒ CHUYỆN:
                {historyBuilder}

                CONTEXT TÀI LIỆU:
                {contextBuilder}

                CÂU HỎI:
                {request.Question}
                """;

                var answerBuilder = new StringBuilder();
                await foreach (var chunk in _geminiService.StreamAnswerAsync(systemPrompt).WithCancellation(cancellationToken))
                {
                    answerBuilder.Append(chunk);
                    var data = JsonSerializer.Serialize(new { text = chunk });
                    await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                var citations = topChunks.Select(c => new CitationDto
                {
                    DocumentId = c.DocumentId,
                    DocumentName = c.Document?.FileName ?? "Tài liệu",
                    PageNumber = c.PageNumber,
                    Snippet = c.Content.Length > 500 ? c.Content.Substring(0, 500).TrimEnd() + "…" : c.Content
                }).ToList();

                _db.ChatMessages.AddRange(
                    new ChatMessage { ChatSessionId = session.Id, Sender = "user", Content = request.Question },
                    new ChatMessage
                    {
                        ChatSessionId = session.Id,
                        Sender = "assistant",
                        Content = answerBuilder.ToString(),
                        CitationsJson = JsonSerializer.Serialize(citations, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    });
                if (session.Title == "Cuộc trò chuyện mới")
                    session.Title = request.Question.Length > 60 ? request.Question[..60] + "…" : request.Question;
                await _db.SaveChangesAsync(cancellationToken);

                var endData = JsonSerializer.Serialize(
                    new { done = true, sessionId = session.Id, citations },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
        // 1. Trang giao diện Tạo Quiz độc lập (URL: /Document/StandaloneQuiz)
        [HttpGet("Document/StandaloneQuiz")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult StandaloneQuiz()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            return View(); // Trỏ tới Views/Document/StandaloneQuiz.cshtml
        }

        // DTO nhận yêu cầu tạo quiz từ file tạm
        public class StandaloneQuizRequestDto
        {
            public IFormFile? File { get; set; }
            public int QuestionCount { get; set; } = 10;
        }

        // 2. API: Nhận file tạm, phân tích và trả về bộ Quiz trực tiếp
        [HttpPost("api/Document/generate-standalone-quiz")]
        public async Task<IActionResult> GenerateStandaloneQuiz([FromForm] StandaloneQuizRequestDto request)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString)) return Unauthorized();

            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest("Vui lòng tải lên một tệp tài liệu.");
            }

            if (!request.File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Chỉ chấp nhận định dạng file PDF.");
            }

            // Lưu file tạm vào thư mục Uploads hoặc thư mục Temp riêng biệt
            var tempFolder = Path.Combine(_env.ContentRootPath, "Uploads", "Temp");
            if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

            var tempFileName = $"{Guid.NewGuid()}_{request.File.FileName}";
            var tempFilePath = Path.Combine(tempFolder, tempFileName);

            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }

                // Trích xuất văn bản trực tiếp từ file PDF tạm thời
                string extractedText = ExtractTextFromPdfFile(tempFilePath);

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    return BadRequest("Không thể trích xuất văn bản từ tài liệu này.");
                }

                // Gọi Gemini AI sinh bộ câu hỏi Quiz dựa trên nội dung text vừa đọc
                var quizQuestions = await _geminiService.GenerateQuizAsync(extractedText, request.QuestionCount);

                if (quizQuestions == null || !quizQuestions.Any())
                {
                    return BadRequest("Không thể tạo Quiz từ nội dung tài liệu này. Vui lòng thử lại.");
                }

                return Ok(quizQuestions);
            }
            finally
            {
                // Dọn dẹp: Xóa file tạm ngay sau khi xử lý xong (hoặc giữ lại tùy nhu cầu phiên làm việc)
                if (System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                }
            }
        }

        // Hàm hỗ trợ bóc tách text nhanh từ file PDF tạm
        private string ExtractTextFromPdfFile(string filePath)
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(filePath);
            var sb = new StringBuilder();
            foreach (var page in pdf.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            // Giới hạn độ dài text gửi lên AI để tránh vượt quá token window nếu file quá dày
            var fullText = sb.ToString();
            return fullText.Length > 25000 ? fullText.Substring(0, 25000) : fullText;
        }
        // 1. Trang giao diện Tóm tắt độc lập (URL: /Document/StandaloneSummary)
        [HttpGet("Document/StandaloneSummary")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult StandaloneSummary()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            return View(); // Trỏ tới Views/Document/StandaloneSummary.cshtml
        }

        // DTO nhận yêu cầu tóm tắt file tạm
        public class StandaloneSummaryRequestDto
        {
            public IFormFile? File { get; set; }
            public string SummaryStyle { get; set; } = "bullets";
        }

        // 2. API: Nhận file tạm, phân tích text và gọi Gemini tóm tắt
        [HttpPost("api/Document/generate-standalone-summary")]
        public async Task<IActionResult> GenerateStandaloneSummary([FromForm] StandaloneSummaryRequestDto request)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString)) return Unauthorized();

            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest("Vui lòng tải lên một tệp tài liệu.");
            }

            if (!request.File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Chỉ chấp nhận định dạng file PDF.");
            }

            // Lưu file tạm vào thư mục Uploads/Temp
            var tempFolder = Path.Combine(_env.ContentRootPath, "Uploads", "Temp");
            if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

            var tempFileName = $"{Guid.NewGuid()}_{request.File.FileName}";
            var tempFilePath = Path.Combine(tempFolder, tempFileName);

            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }

                // Trích xuất văn bản từ file PDF tạm
                string extractedText = ExtractTextFromPdfFile(tempFilePath);

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    return BadRequest("Không thể trích xuất văn bản từ tài liệu này.");
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

                VĂN BẢN GỐC CẦN TÓM TẮT:
                {extractedText}
                """;

                var summaryResult = await _geminiService.GenerateAnswerAsync(prompt);
                return Ok(new { summary = summaryResult });
            }
            finally
            {
                // Dọn dẹp: Xóa file tạm ngay sau khi xử lý xong phiên làm việc
                if (System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                }
            }
        }
        // 1. Trang giao diện Hỏi đáp nhanh độc lập (URL: /Document/EphemeralChat)
        [HttpGet("Document/EphemeralChat")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult EphemeralChat()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            return View(); // Trỏ tới Views/Document/EphemeralChat.cshtml
        }

        // DTO nhận file upload tạm cho phiên chat nhanh
        public class EphemeralInitRequestDto
        {
            public IFormFile? File { get; set; }
        }

        // 2. API: Khởi tạo phiên chat nhanh, bóc tách và embedding vector tạm thời
        [HttpPost("api/Document/ephemeral/init")]
        public async Task<IActionResult> InitEphemeralChat([FromForm] EphemeralInitRequestDto request)
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString)) return Unauthorized();

            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest("Vui lòng tải lên tài liệu PDF.");
            }

            if (!request.File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Chỉ chấp nhận định dạng file PDF.");
            }

            Guid userId = Guid.Parse(userIdString);
            Guid ephemeralDocId = Guid.NewGuid();

            // Lưu file tạm vào thư mục Uploads/Temp
            var tempFolder = Path.Combine(_env.ContentRootPath, "Uploads", "Temp");
            if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

            var tempFileName = $"{ephemeralDocId}_{request.File.FileName}";
            var tempFilePath = Path.Combine(tempFolder, tempFileName);

            using (var stream = new FileStream(tempFilePath, FileMode.Create))
            {
                await request.File.CopyToAsync(stream);
            }

            // Tạo một bản ghi Document tạm trong Database để tận dụng hệ thống Embedding & Retrieval sẵn có
            var tempDoc = new Document
            {
                Id = ephemeralDocId,
                UserId = userId,
                FileName = request.File.FileName,
                FilePath = tempFilePath,
                UploadedAt = DateTime.UtcNow,
                Status = "Processing"
            };

            _db.Documents.Add(tempDoc);
            await _db.SaveChangesAsync();

            try
            {
                // Thực hiện bóc tách PDF và tạo vector embedding ngay lập tức cho phiên này
                var processor = HttpContext.RequestServices.GetRequiredService<DocumentProcessingJob>();
                await processor.ProcessPdfAsync(ephemeralDocId); // Hàm xử lý parse và tạo chunks + vector

                return Ok(new { documentId = ephemeralDocId, fileName = request.File.FileName });
            }
            catch (Exception ex)
            {
                // Xóa bản ghi và file nếu lỗi
                _db.Documents.Remove(tempDoc);
                await _db.SaveChangesAsync();
                if (System.IO.File.Exists(tempFilePath)) System.IO.File.Delete(tempFilePath);

                return BadRequest("Lỗi xử lý tài liệu tạm: " + ex.Message);
            }
        }

        // DTO yêu cầu câu hỏi chat nhanh
        public class EphemeralChatRequestDto
        {
            public Guid DocumentId { get; set; }
            public string Question { get; set; } = string.Empty;
        }

        // 3. API: Hỏi đáp RAG thời gian thực dựa trên tài liệu tạm (Stream SSE)
        [HttpPost("api/Document/ephemeral/chat-stream")]
        public async Task EphemeralChatStream([FromBody] EphemeralChatRequestDto request, CancellationToken cancellationToken)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

            try
            {
                // Sử dụng RetrievalService tìm kiếm các đoạn chunk vector liên quan trong tài liệu tạm
                var topChunks = await _retrievalService.GetRelevantParentChunksAsync(
                    request.Question,
                    topK: 5,
                    documentIds: new List<Guid> { request.DocumentId }
                );

                var contextBuilder = new StringBuilder();
                foreach (var chunk in topChunks)
                {
                    contextBuilder.AppendLine(chunk.Content);
                    contextBuilder.AppendLine();
                }

                var systemPrompt = $"""
                Bạn là trợ lý AI thông minh. Hãy trả lời câu hỏi dựa hoàn toàn vào NGỮ CẢNH TÀI LIỆU tạm thời dưới đây.
                Nếu thông tin không có trong ngữ cảnh, hãy trả lời "Tài liệu không đề cập đến vấn đề này."

                NGỮ CẢNH TÀI LIỆU:
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

                var endData = JsonSerializer.Serialize(new { done = true });
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

        // 4. API: Dọn dẹp và xóa sạch tài liệu tạm, vector, file khi người dùng rời phiên hoặc kết thúc
        [HttpDelete("api/Document/ephemeral/cleanup/{id:guid}")]
        public async Task<IActionResult> CleanupEphemeralSession(Guid id)
        {
            var doc = await _db.Documents.FindAsync(id);
            if (doc != null)
            {
                // Xóa file vật lý tạm
                if (!string.IsNullOrEmpty(doc.FilePath) && System.IO.File.Exists(doc.FilePath))
                {
                    System.IO.File.Delete(doc.FilePath);
                }

                // Xóa bản ghi Document (Cascade delete sẽ tự động xóa các DocumentChunks và Vector embeddings liên quan nếu đã cấu hình)
                _db.Documents.Remove(doc);
                await _db.SaveChangesAsync();
            }

            return Ok(new { message = "Đã dọn dẹp sạch dữ liệu phiên." });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id, string returnUrl = "List")
        {
            var document = await _db.Documents
                .Include(d => d.ParentChunks)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
            {
                TempData["Error"] = "Không tìm thấy tài liệu cần xóa.";
                return RedirectToAction(returnUrl == "Index" ? "Index" : "List");
            }

            try
            {
                // 1. Xóa file vật lý trên ổ đĩa server nếu có (bọc try-catch phòng file đang bị Hangfire giữ)
                if (!string.IsNullOrEmpty(document.FilePath) && System.IO.File.Exists(document.FilePath))
                {
                    try
                    {
                        System.IO.File.Delete(document.FilePath);
                    }
                    catch
                    {
                        // Bỏ qua nếu file đang bị tiến trình ngầm đọc dở dang
                    }
                }

                // 2. Xóa dữ liệu khỏi Database (Cascade Delete sẽ tự động dọn sạch chunks)
                _db.Documents.Remove(document);
                await _db.SaveChangesAsync();

                TempData["Success"] = $"Đã xóa thành công tài liệu: {document.FileName}";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi khi xóa tài liệu: {ex.Message}";
            }

            // Trở về đúng trang nguồn mà người dùng đã bấm xóa (Mặc định là "List")
            if (returnUrl == "Index")
            {
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(List));
        }
    }
}
