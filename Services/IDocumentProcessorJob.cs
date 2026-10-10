namespace SmartDocumentRAG.API.Services;

public interface IDocumentProcessorJob
{
    Task ProcessPdfAsync(Guid documentId);
}
