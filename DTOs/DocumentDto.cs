namespace SmartDocumentRAG.API.DTOs;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public int TotalPages { get; set; }
}
