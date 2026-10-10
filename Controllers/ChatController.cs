using Microsoft.AspNetCore.Mvc;
using SmartDocumentRAG.API.DTOs;
using SmartDocumentRAG.API.Services;

namespace SmartDocumentRAG.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatRAGService _chatRagService;
    private readonly IRetrievalService _retrievalService;

    public ChatController(IChatRAGService chatRagService, IRetrievalService retrievalService)
    {
        _chatRagService = chatRagService;
        _retrievalService = retrievalService;
    }

    /// <summary>
    /// Gửi câu hỏi đến hệ thống RAG và nhận câu trả lời kèm Trích dẫn (Citations)
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<ChatResponseDto>> Query([FromBody] ChatRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { message = "Vui lòng nhập câu hỏi." });
        }

        var response = await _chatRagService.AskQuestionAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// Kiểm thử Hybrid Search & Parent Retrieval mà không cần sinh lời văn từ LLM
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<SearchResultDto>>> Search([FromQuery] string query, [FromQuery] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { message = "Vui lòng nhập từ khóa tìm kiếm." });
        }

        var results = await _retrievalService.HybridSearchAsync(query, topK);
        return Ok(results);
    }
}
