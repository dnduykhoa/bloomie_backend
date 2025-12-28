using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        public string? Link { get; set; }

        [Required]
        public string Type { get; set; } = "info"; // success, info, warning, danger

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Null = thông báo cho tất cả admin
        // Có giá trị = thông báo riêng cho user cụ thể
        public string? UserId { get; set; }
    }
}
