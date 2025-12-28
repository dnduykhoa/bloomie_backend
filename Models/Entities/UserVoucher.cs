using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class UserVoucher
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [Required]
        public int PromotionCodeId { get; set; }

        [ForeignKey("PromotionCodeId")]
        public PromotionCode? PromotionCode { get; set; }

        [Required]
        [StringLength(50)]
        public string Source { get; set; } = string.Empty; // LuckyWheel, FlashSale, Birthday, Referral, AdminGift, Welcome

        [Required]
        public DateTime CollectedDate { get; set; }

        [Required]
        public DateTime ExpiryDate { get; set; }

        public bool IsUsed { get; set; } = false;

        public DateTime? UsedDate { get; set; }

        public int? OrderId { get; set; }

        [ForeignKey("OrderId")]
        public Order? Order { get; set; }

        [StringLength(500)]
        public string? Note { get; set; } // Ghi chú từ admin khi tặng thủ công
    }
}
