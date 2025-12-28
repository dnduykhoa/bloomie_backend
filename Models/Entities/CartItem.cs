using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class CartItem
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        // Đã bỏ FlowerVariantId vì không cần lưu biến thể hoa cho mỗi item trong giỏ hàng
        public int Quantity { get; set; }
        public string? Note { get; set; }
        public Product Product { get; set; }
        // Đã bỏ FlowerVariant navigation property
        public DateTime? DeliveryDate { get; set; } // Ngày giao hàng riêng cho từng bó
        public string? DeliveryTime { get; set; }   // Khung giờ giao hàng riêng cho từng bó

        // Số tiền giảm giá áp dụng cho sản phẩm này (nếu có)
        public decimal? Discount { get; set; }
        // Đánh dấu sản phẩm là quà tặng (mua X tặng Y)
        public bool IsGift { get; set; } = false;
        
        // UserId để lưu giỏ hàng theo người dùng
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
    }
}
