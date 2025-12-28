using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class UnifiedPromotionVM
    {
        public string Type { get; set; } = string.Empty; // "Campaign", "ProductDiscount", "PointReward"
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string SubType { get; set; } = string.Empty; // "FlashSale", "LuckyWheel", "Percent", "Fixed", etc.
        public string Value { get; set; } = string.Empty; // "50K", "30%", "100 điểm"
        public string AppliesTo { get; set; } = string.Empty; // "All", "15 sản phẩm", "5 danh mục"
        public string TimeRange { get; set; } = string.Empty; // "01/01/2024 - 31/12/2024"
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; }
        public string Status { get; set; } = string.Empty; // "Đang chạy", "Chưa bắt đầu", "Đã kết thúc", "Tạm dừng"
        public string StatusClass { get; set; } = string.Empty; // "success", "warning", "danger", "secondary"
        public string Stats { get; set; } = string.Empty; // Thống kê
        public string BadgeClass { get; set; } = string.Empty; // "bg-danger", "bg-warning", "bg-primary", "bg-success"
        public string Icon { get; set; } = string.Empty; // "fa-bolt", "fa-dharmachakra", "fa-tags", "fa-gift"
        
        // Thuộc tính gốc để dùng cho Edit/Delete
        public object? OriginalEntity { get; set; }
    }

    public class ManageCampaignsVM
    {
        public List<UnifiedPromotionVM> Promotions { get; set; } = new();
        public int TotalCampaigns { get; set; }
        public int TotalProductDiscounts { get; set; }
        public int TotalPointRewards { get; set; }
        public int ActiveCount { get; set; }
    }
}
