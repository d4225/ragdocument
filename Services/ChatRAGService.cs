using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Models;

namespace SmartDocumentRAG.API.Services;

public class ChatRAGService : IChatRAGService
{
    private readonly ApplicationDbContext _db;
    private readonly IRetrievalService _retrievalService;
    private readonly IGeminiService _geminiService;

    public ChatRAGService(
        ApplicationDbContext db,
        IRetrievalService retrievalService,
        IGeminiService geminiService)
    {
        _db = db;
        _retrievalService = retrievalService;
        _geminiService = geminiService;
    }

    public async Task<ChatResponseDto> AskQuestionAsync(ChatRequestDto request)
    {
        // 1. Quản lý Session
        ChatSession session;
        if (!request.SessionId.HasValue || request.SessionId == Guid.Empty)
        {
            session = new ChatSession
            {
                Title = request.Question.Length > 30 ? request.Question.Substring(0, 30) + "..." : request.Question
            };
            _db.ChatSessions.Add(session);
            await _db.SaveChangesAsync();
        }
        else
        {
            session = await _db.ChatSessions
                .Include(s => s.Messages.OrderByDescending(m => m.CreatedAt).Take(6))
                .FirstOrDefaultAsync(s => s.Id == request.SessionId.Value)
                ?? throw new Exception("Không tìm thấy phiên trò chuyện.");
        }

        // 2. Lấy 6 tin nhắn gần nhất làm History
        var recentMessages = session.Messages.OrderBy(m => m.CreatedAt).ToList();

        // 3. Chạy Hybrid Search tìm Chunks liên quan
        var topChunks = await _retrievalService.GetRelevantParentChunksAsync(request.Question, topK: request.TopK);

        // 4. Dựng Prompt bao gồm Context + Chat History
        var historyBuilder = new StringBuilder();
        foreach (var msg in recentMessages)
        {
            var role = msg.Sender == "user" ? "Người dùng" : "Trợ lý AI";
            historyBuilder.AppendLine($"{role}: {msg.Content}");
        }

        var contextBuilder = new StringBuilder();
        foreach (var chunk in topChunks)
        {
            var fileName = chunk.Document?.FileName ?? "Tài liệu";
            contextBuilder.AppendLine($"--- Nguồn: {fileName} ---");
            contextBuilder.AppendLine(chunk.Content);
            contextBuilder.AppendLine();
        }

        var systemPrompt = $"""
            Bạn là trợ lý AI chuyên phân tích văn bản. Trả lời câu hỏi dựa trên CONTEXT được cung cấp.
            Nếu thông tin không nằm trong Context, hãy lịch sự trả lời "Tài liệu không đề cập".

            LỊCH SỬ TRÒ CHUYỆN GẦN ĐÂY:
            {historyBuilder}

            CONTEXT TÀI LIỆU:
            {contextBuilder}

            CÂU HỎI HIỆN TẠI:
            {request.Question}
            """;

        // 5. Gọi Gemini
        var answer = await _geminiService.GenerateAnswerAsync(systemPrompt);

        // 6. Lưu câu hỏi & câu trả lời vào ChatMessage Table
        var userMsg = new ChatMessage
        {
            ChatSessionId = session.Id,
            Sender = "user",
            Content = request.Question
        };

        var citations = topChunks.Select(c => new CitationDto
        {
            DocumentName = c.Document?.FileName ?? "Tài liệu",
            Snippet = c.Content.Length > 150 ? c.Content.Substring(0, 150) + "..." : c.Content
        }).ToList();

        var aiMsg = new ChatMessage
        {
            ChatSessionId = session.Id,
            Sender = "assistant",
            Content = answer,
            CitationsJson = JsonSerializer.Serialize(citations)
        };

        _db.ChatMessages.AddRange(userMsg, aiMsg);
        await _db.SaveChangesAsync();

        return new ChatResponseDto
        {
            SessionId = session.Id,
            Answer = answer,
            Citations = citations
        };
    }
}