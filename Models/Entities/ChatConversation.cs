using System.ComponentModel.DataAnnotations;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class ChatConversation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty; // User tham gia chat

        public ApplicationUser? User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? LastMessageAt { get; set; }

        public bool IsActive { get; set; } = true;

        // Navigation
        public ICollection<ChatMessage>? Messages { get; set; }
    }
}
