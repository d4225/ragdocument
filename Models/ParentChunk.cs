using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartDocumentRAG.API.Models;

public class ParentChunk
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }
    [ForeignKey(nameof(DocumentId))]
    public Document Document { get; set; } = null!;

    [Required]
    public string Content { get; set; } = string.Empty;

    public int PageNumber { get; set; }

    public ICollection<ChildChunk> ChildChunks { get; set; } = new List<ChildChunk>();
}