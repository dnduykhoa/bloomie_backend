namespace Bloomie.Models.Entities
{
    public class PromotionGiftBuyCategory
    {
        public int Id { get; set; }
        public int PromotionGiftId { get; set; }
        public int CategoryId { get; set; }
        public PromotionGift PromotionGift { get; set; }
        public Category Category { get; set; }
    }
}