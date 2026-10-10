using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace SmartDocumentRAG.API.Models;

public class ChildChunk
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ParentChunkId { get; set; }

    [ForeignKey(nameof(ParentChunkId))]
    public ParentChunk ParentChunk { get; set; } = null!;

    [Required]
    public string Content { get; set; } = string.Empty;

    public Vector? Embedding { get; set; }

    public int ChunkIndex { get; set; }
}
