using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bloomie.Models.Entities
{
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        public string? SessionId { get; set; } // Để track conversation

        public int? ChatConversationId { get; set; } // Foreign key to ChatConversation

        [Required]
        public string Message { get; set; } = string.Empty;

        [Required]
        public bool IsBot { get; set; } // true = bot response, false = user message

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string? UserId { get; set; } // Optional: nếu user đăng nhập

        public string? Intent { get; set; } // Ý định của câu hỏi (product_inquiry, price_query, etc.)

        public string? Metadata { get; set; } // JSON data for quick replies, buttons, etc.
    }
}
