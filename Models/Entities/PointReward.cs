using System;

namespace Bloomie.Models.Entities
{
    public enum RewardType
    {
        Voucher = 0,
        FreeShip = 1,
        Discount = 2
    }

    public class PointReward
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public int PointsCost { get; set; }
        public int? PromotionCodeId { get; set; }
        public PromotionCode? PromotionCode { get; set; }
        public RewardType RewardType { get; set; }
        public int? Stock { get; set; }
        public bool IsActive { get; set; } = true;
        public int ValidDays { get; set; } = 30;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public string? ImageUrl { get; set; }
    }
}
