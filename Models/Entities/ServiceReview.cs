using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class ServiceReview
    {
        [Key]
        public int ServiceReviewId { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public Order? Order { get; set; }

        [Required]
        public string? UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        // Đánh giá dịch vụ giao hàng (1-5 sao)
        [Range(1, 5)]
        public int DeliveryRating { get; set; }

        // Đánh giá chăm sóc khách hàng (1-5 sao)
        [Range(1, 5)]
        public int ServiceRating { get; set; }

        // Đánh giá tổng thể (1-5 sao)
        [Range(1, 5)]
        public int OverallRating { get; set; }

        // Nhận xét
        [StringLength(1000)]
        public string? Comment { get; set; }

        // Hình ảnh đính kèm (nếu có)
        public string? ImageUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
