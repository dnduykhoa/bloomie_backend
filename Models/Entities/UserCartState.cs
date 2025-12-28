using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class UserCartState
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = null!;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        public string? PromotionCode { get; set; }  // ⭐ Lưu mã voucher

        public int? PromotionId { get; set; }  // ⭐ Lưu ID để validate

        [Column(TypeName = "decimal(18,2)")]
        public decimal? DiscountAmount { get; set; }  // ⭐ Lưu discount

        public bool FreeShipping { get; set; }  // ⭐ Lưu free ship

        public DateTime? AppliedAt { get; set; }  // ⭐ Lưu thời gian áp dụng

        public DateTime? LastUpdated { get; set; }
    }
}
