namespace Bloomie.Models.Entities
{
    public class PromotionGift
    {
        public int Id { get; set; }
        public int PromotionId { get; set; }
        public int BuyQuantity { get; set; }
        public int GiftQuantity { get; set; }
        public string BuyConditionType { get; set; } // MinQuantity hoặc MinValue
        public int? BuyConditionValue { get; set; } // Số lượng tối thiểu
        public decimal? BuyConditionValueMoney { get; set; } // Giá trị tối thiểu (VNĐ)
        public string BuyApplyType { get; set; } // product hoặc category
        public string GiftApplyType { get; set; } // product hoặc category
        public string GiftDiscountType { get; set; } // percent, money, hoặc free
        public int? GiftDiscountValue { get; set; } // Phần trăm giảm (0-100)
        public decimal? GiftDiscountMoneyValue { get; set; } // Giá trị tiền giảm (VNĐ)
        public bool LimitPerOrder { get; set; } // Giới hạn số lần áp dụng trong đơn
        public Promotion? Promotion { get; set; }
        public ICollection<PromotionGiftBuyProduct>? BuyProducts { get; set; }
        public ICollection<PromotionGiftBuyCategory>? BuyCategories { get; set; }
        public ICollection<PromotionGiftGiftProduct>? GiftProducts { get; set; }
        public ICollection<PromotionGiftGiftCategory>? GiftCategories { get; set; }
    }
}