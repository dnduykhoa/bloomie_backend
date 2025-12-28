using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int? ParentId { get; set; }

    public string? Description { get; set; }

    public int Type { get; set; }

    public virtual ICollection<Category> InverseParent { get; set; } = new List<Category>();

    public virtual Category? Parent { get; set; }

    public virtual ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();

    public virtual ICollection<PromotionCategory> PromotionCategories { get; set; } = new List<PromotionCategory>();

    public virtual ICollection<PromotionGiftBuyCategory> PromotionGiftBuyCategories { get; set; } = new List<PromotionGiftBuyCategory>();

    public virtual ICollection<PromotionGiftGiftCategory> PromotionGiftGiftCategories { get; set; } = new List<PromotionGiftGiftCategory>();
}
