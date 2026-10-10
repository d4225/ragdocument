using System.ComponentModel.DataAnnotations;

namespace SmartDocumentRAG.API.Models;

public class Document
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string FilePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    public string? ErrorMessage { get; set; }

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ParentChunk> ParentChunks { get; set; } = new List<ParentChunk>();
}