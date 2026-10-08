using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartDocumentRAG.API.Models;

public class Document
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public string Status { get; set; } = "Processing"; // Processing, Completed, Failed

    public ICollection<ParentChunk> ParentChunks { get; set; } = new List<ParentChunk>();
}