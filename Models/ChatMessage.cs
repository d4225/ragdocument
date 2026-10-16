namespace SmartDocumentRAG.API.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChatSessionId { get; set; }
    public string Sender { get; set; } = "user"; // "user" hoặc "assistant"
    public string Content { get; set; } = string.Empty;
    public string? CitationsJson { get; set; } // Lưu JSON mảng CitationDto nếu là assistant
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ChatSession ChatSession { get; set; } = null!;
}