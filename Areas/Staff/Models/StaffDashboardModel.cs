using Bloomie.Models.Entities;

namespace Bloomie.Areas.Staff.Models
{
    public class OrderSummary
    {
        public string Id { get; set; } = "";
        public string TenNguoiDung { get; set; } = "";
        public DateTime OrderDate { get; set; }
        public decimal TotalPrice { get; set; }
        public string OrderStatus { get; set; } = "";
    }

    public class RatingSummary
    {
        public int Id { get; set; }
        public string TenNguoiDung { get; set; } = "";
        public string TenSanPham { get; set; } = "";
        public int GiaTriDanhGia { get; set; }
        public DateTime ReviewDate { get; set; }
        public bool IsVisible { get; set; }
    }
}