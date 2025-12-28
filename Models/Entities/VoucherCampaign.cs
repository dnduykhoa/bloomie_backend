using System;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public enum CampaignType
    {
        FlashSale,
        LuckyWheel,
        Birthday,
        Referral,
        CheckIn
    }

    public class VoucherCampaign
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public CampaignType Type { get; set; }

        public string? Description { get; set; }

        // Liên kết tới PromotionCode để phát
        public int PromotionCodeId { get; set; }
        public PromotionCode? PromotionCode { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        // JSON config cho từng loại campaign
        // FlashSale: { "MaxVouchersPerUser": 1, "TotalVouchers": 100 }
        // LuckyWheel: { "SpinsPerUser": 3, "WinRates": [{"VoucherId": 1, "Rate": 0.1}, ...] }
        // Birthday: { "DaysBefore": 7 }
        // Referral: { "ReferrerVoucherId": 1, "RefereeVoucherId": 2 }
        // CheckIn: { "StreakDays": 7, "RewardVoucherId": 1 }
        public string? Config { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public int CollectedCount { get; set; } = 0; // Số lượng đã phát

        public string? BannerImage { get; set; } // Banner cho campaign
    }
}
