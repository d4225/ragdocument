namespace SmartDocumentRAG.API.DTOs;

public class ChatRequestDto
{
    public Guid? SessionId { get; set; }
    public string Question { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}

public class CitationDto
{
    public Guid? DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string Snippet { get; set; } = string.Empty;
}

public class ChatResponseDto
{
    public Guid SessionId { get; set; }
    public string Answer { get; set; } = string.Empty;
    public List<CitationDto> Citations { get; set; } = new();
}
public class ChatMessageDto
{
    public string Sender { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}