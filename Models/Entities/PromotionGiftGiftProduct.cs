namespace Bloomie.Models.Entities
{
    public class PromotionGiftGiftProduct
    {
        public int Id { get; set; }
        public int PromotionGiftId { get; set; }
        public int ProductId { get; set; }
        public PromotionGift PromotionGift { get; set; }
        public Product Product { get; set; }
    }
}