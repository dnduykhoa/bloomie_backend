using System;
using System.Collections.Generic;

namespace Bloomie.Models.Entities
{
    public class Promotion {
    
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public PromotionType Type { get; set; } // Enum: Product, Order, Shipping, Gift, ...
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public ICollection<PromotionCode>? PromotionCodes { get; set; }
    public ICollection<PromotionGift>? PromotionGifts { get; set; }
    public ICollection<PromotionProduct>? PromotionProducts { get; set; }
    public ICollection<PromotionCategory>? PromotionCategories { get; set; }
    public ICollection<PromotionShipping>? PromotionShippings { get; set; }
    // Trường nâng cao
    public bool AllowCombineOrder { get; set; }
    public bool AllowCombineProduct { get; set; }
    public bool AllowCombineShipping { get; set; }
    public string? ConditionType { get; set; }
    public decimal? ConditionValue { get; set; }

    // Lưu riêng từng giá trị điều kiện
    public decimal? MinOrderValue { get; set; }
    public decimal? MinProductValue { get; set; }
    public int? MinProductQuantity { get; set; }
    // --- Các trường cho khuyến mãi phí vận chuyển nâng cao ---
    public string? ShippingDiscountType { get; set; } // "free", "money", "percent"
    public decimal? ShippingDiscountValue { get; set; }
    public string? ApplyDistricts { get; set; } // Lưu JSON danh sách quận/huyện
    public decimal? ApplyRadiusKm { get; set; }
    }
    public enum PromotionType
    {
        Product = 0,
        Order = 1,
        Shipping = 2,
        Gift = 3
    }
}
