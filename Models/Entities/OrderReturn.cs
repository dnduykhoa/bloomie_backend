using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bloomie.Models.Entities
{
    public class OrderReturn
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public Order? Order { get; set; }

        [Required]
        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Chờ xử lý"; // Chờ xử lý, Chấp nhận, Từ chối, Đã hoàn tiền

        public DateTime RequestDate { get; set; } = DateTime.Now;

        public DateTime? ResponseDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? RefundAmount { get; set; }

        [StringLength(1000)]
        public string? Images { get; set; } // Lưu nhiều ảnh, phân cách bằng dấu ;

        [StringLength(1000)]
        public string? AdminNote { get; set; } // Ghi chú từ admin

        [StringLength(50)]
        public string? ReturnType { get; set; } = "Hoàn tiền"; // Hoàn tiền, Đổi hàng
    }
}
