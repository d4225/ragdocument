using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
//using NpgsqlTypes;
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

    // Vector embedding từ Gemini
    public Pgvector.Vector? Embedding { get; set; }

    // PostgreSQL Full-Text Search
    public NpgsqlTypes.NpgsqlTsVector SearchVector { get; set; } = null!;
}