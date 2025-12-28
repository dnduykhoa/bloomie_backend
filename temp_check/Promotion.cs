using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class Promotion
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int Type { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; }

    public bool AllowCombineOrder { get; set; }

    public bool AllowCombineProduct { get; set; }

    public bool AllowCombineShipping { get; set; }

    public string? ConditionType { get; set; }

    public decimal? ConditionValue { get; set; }

    public decimal? MinOrderValue { get; set; }

    public int? MinProductQuantity { get; set; }

    public decimal? MinProductValue { get; set; }

    public string? ApplyDistricts { get; set; }

    public decimal? ApplyRadiusKm { get; set; }

    public string? ShippingDiscountType { get; set; }

    public decimal? ShippingDiscountValue { get; set; }

    public virtual ICollection<PromotionCategory> PromotionCategories { get; set; } = new List<PromotionCategory>();

    public virtual ICollection<PromotionCode> PromotionCodes { get; set; } = new List<PromotionCode>();

    public virtual ICollection<PromotionGift> PromotionGifts { get; set; } = new List<PromotionGift>();

    public virtual ICollection<PromotionOrder> PromotionOrders { get; set; } = new List<PromotionOrder>();

    public virtual ICollection<PromotionProduct> PromotionProducts { get; set; } = new List<PromotionProduct>();

    public virtual ICollection<PromotionShipping> PromotionShippings { get; set; } = new List<PromotionShipping>();
}
