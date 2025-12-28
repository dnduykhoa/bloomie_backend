using Bloomie.Models.ViewModels;
using Bloomie.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.ApiControllers
{
    [Route("api/Chat")]
    [ApiController]
    [Authorize]
    public class ChatApiController : ControllerBase
    {
        private readonly IChatBotService _chatBotService;
        private readonly ApplicationDbContext _context;

        public ChatApiController(IChatBotService chatBotService, ApplicationDbContext context)
        {
            _chatBotService = chatBotService;
            _context = context;
        }

        [HttpPost("message")]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            Console.WriteLine($"[ChatAPI] Received message: {request?.Message}");
            
            if (string.IsNullOrWhiteSpace(request?.Message))
            {
                Console.WriteLine("[ChatAPI] Message is empty");
                return BadRequest(new { error = "Message cannot be empty" });
            }

            try
            {
                // Get user ID from claims if authenticated
                var userId = User?.Claims?.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                request.UserId = userId;

                Console.WriteLine($"[ChatAPI] Processing message for user: {userId ?? "anonymous"}");
                var response = await _chatBotService.ProcessMessageAsync(request);
                Console.WriteLine($"[ChatAPI] Response generated: {response.Message.Substring(0, Math.Min(50, response.Message.Length))}...");
                
                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatAPI] Error: {ex.Message}");
                Console.WriteLine($"[ChatAPI] Stack trace: {ex.StackTrace}");
                return StatusCode(500, new { error = "An error occurred", details = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] string? sessionId, [FromQuery] int limit = 50)
        {
            try
            {
                // Get user ID from claims if authenticated
                var userId = User?.Claims?.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                
                if (string.IsNullOrEmpty(sessionId) && string.IsNullOrEmpty(userId))
                {
                    return BadRequest(new { error = "SessionId or UserId is required" });
                }

                IQueryable<Bloomie.Models.Entities.ChatMessage> query = _context.ChatMessages;

                // Filter by sessionId or userId
                if (!string.IsNullOrEmpty(sessionId))
                {
                    query = query.Where(m => m.SessionId == sessionId);
                }
                else if (!string.IsNullOrEmpty(userId))
                {
                    query = query.Where(m => m.UserId == userId);
                }

                // Get latest messages ordered by creation time (newest first, then reverse)
                var allMessages = await query
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(limit)
                    .ToListAsync();

                // Reverse to show oldest first (chronological order)
                var messages = allMessages
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new
                    {
                        id = m.Id,
                        message = m.Message,
                        isBot = m.IsBot,
                        createdAt = m.CreatedAt,
                        intent = m.Intent,
                        metadata = m.Metadata
                    })
                    .ToList();

                return Ok(new { messages, count = messages.Count });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatAPI] Error getting history: {ex.Message}");
                return StatusCode(500, new { error = "An error occurred", details = ex.Message });
            }
        }
    }
}
