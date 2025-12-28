namespace Bloomie.Models.Entities
{
    public class OrderDetail
    {
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    // Đã bỏ FlowerVariantId vì không cần lưu biến thể hoa cho mỗi đơn hàng
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Note { get; set; }
    public Order Order { get; set; }
    public Product Product { get; set; }
    // Đã bỏ FlowerVariant navigation property
    public DateTime? DeliveryDate { get; set; } // Ngày giao hàng riêng cho từng sản phẩm
    public string? DeliveryTime { get; set; }   // Khung giờ giao hàng riêng cho từng sản phẩm
    
    // Đánh dấu sản phẩm là quà tặng (mua X tặng Y)
    public bool IsGift { get; set; } = false;
    }
}
