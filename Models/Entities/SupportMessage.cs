using System.ComponentModel.DataAnnotations;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    /// <summary>
    /// Tin nhắn trong Support Chat (Customer ↔ Staff)
    /// </summary>
    public class SupportMessage
    {
        [Key]
        public int Id { get; set; }

        public int ConversationId { get; set; }
        public SupportConversation? Conversation { get; set; }

        [Required]
        public string SenderId { get; set; } = string.Empty; // UserId của người gửi

        public ApplicationUser? Sender { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.Now;

        public bool IsRead { get; set; } = false;
        
        public DateTime? ReadAt { get; set; } // Thời điểm đã xem

        public bool IsFromStaff { get; set; } = false; // true = Staff gửi, false = Customer gửi
        
        public string? AttachmentUrl { get; set; } // Optional: link ảnh/file đính kèm
    }
}
