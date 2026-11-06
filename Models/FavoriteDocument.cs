namespace SmartDocumentRAG.API.Models;

public class FavoriteDocument
{
    public Guid UserId { get; set; }
    public Guid DocumentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
