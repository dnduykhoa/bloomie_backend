namespace Bloomie.Models.Entities
{
    public class PromotionShipping
    {
        public int Id { get; set; }
        public int PromotionId { get; set; }
        public Promotion Promotion { get; set; }
        public decimal? ShippingDiscount { get; set; }
    }
}
