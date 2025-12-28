using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public class ShippingFee
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Mã phường/xã là bắt buộc")]
        [StringLength(10)]
        public string WardCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tên phường/xã là bắt buộc")]
        [StringLength(100)]
        public string WardName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phí ship là bắt buộc")]
        [Range(0, 1000000, ErrorMessage = "Phí ship phải từ 0 đến 1,000,000đ")]
        public decimal Fee { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? UpdatedAt { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }
}
