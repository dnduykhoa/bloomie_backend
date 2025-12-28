namespace Bloomie.Models.Entities
{
    public class PromotionOrder
    {
        public int Id { get; set; }
        public int PromotionId { get; set; }
        public int OrderId { get; set; }
        public Promotion Promotion { get; set; }
        // public Order Order { get; set; } // Thêm khi có bảng Order
    }
}
