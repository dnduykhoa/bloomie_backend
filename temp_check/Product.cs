using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class Product
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public string? ImageUrl { get; set; }

    public int StockQuantity { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

    public virtual ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();

    public virtual ICollection<ProductDetail> ProductDetails { get; set; } = new List<ProductDetail>();

    public virtual ICollection<ProductImage> ProductImages { get; set; } = new List<ProductImage>();

    public virtual ICollection<PromotionGiftBuyProduct> PromotionGiftBuyProducts { get; set; } = new List<PromotionGiftBuyProduct>();

    public virtual ICollection<PromotionGiftGiftProduct> PromotionGiftGiftProducts { get; set; } = new List<PromotionGiftGiftProduct>();

    public virtual ICollection<PromotionProduct> PromotionProducts { get; set; } = new List<PromotionProduct>();

    public virtual ICollection<Rating> Ratings { get; set; } = new List<Rating>();
}
