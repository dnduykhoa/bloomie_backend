using System;

namespace Bloomie.Models.Entities
{
    public class PromotionCode
    {
    public int Id { get; set; }
    public string Code { get; set; }
    public int? PromotionId { get; set; }
    public Promotion? Promotion { get; set; }
    public decimal? Value { get; set; } // Giá trị giảm giá hoặc tặng
    public bool IsPercent { get; set; }
    public decimal? MaxDiscount { get; set; }
    public int? UsageLimit { get; set; } // Số lần sử dụng tối đa
    public int UsedCount { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsActive { get; set; }
    public bool LimitPerCustomer { get; set; }
    public int? MinOrderValue { get; set; }
    public bool IsPublic { get; set; } = false; // Hiển thị trên trang Vouchers công khai
    }
}
