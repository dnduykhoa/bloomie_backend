using System.ComponentModel.DataAnnotations;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    /// <summary>
    /// Conversation giữa Customer (User) và Staff (Admin/Manager/Staff)
    /// </summary>
    public class SupportConversation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string CustomerId { get; set; } = string.Empty; // User (khách hàng)

        public ApplicationUser? Customer { get; set; }

        public string? StaffId { get; set; } // Staff đang xử lý (Admin/Manager/Staff)
        
        public ApplicationUser? Staff { get; set; }

        [MaxLength(1000)] // Đủ chứa attachment JSON
        public string? LastMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? LastMessageAt { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsClosed { get; set; } = false; // Đã đóng conversation chưa

        public int UnreadByStaff { get; set; } = 0; // Tin nhắn chưa đọc bởi staff

        public int UnreadByCustomer { get; set; } = 0; // Tin nhắn chưa đọc bởi customer

        // Tags for categorization
        [MaxLength(50)]
        public string? Tag { get; set; } // "Tư vấn", "Khiếu nại", "VIP", "Đặt hàng", "Thanh toán"

        // Priority level
        public int Priority { get; set; } = 0; // 0: Normal, 1: High, 2: Urgent
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public ICollection<SupportMessage>? Messages { get; set; }
    }
}
