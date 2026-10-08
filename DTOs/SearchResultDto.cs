namespace SmartDocumentRAG.API.DTOs;

public class SearchResultDto
{
    public Guid ChildChunkId { get; set; }
    public Guid ParentChunkId { get; set; }
    public string ChildContent { get; set; } = string.Empty;
    public string ParentContent { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public double Score { get; set; }
}