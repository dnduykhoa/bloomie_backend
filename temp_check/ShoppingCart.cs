using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class ShoppingCart
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal? DiscountAmount { get; set; }

    public bool FreeShipping { get; set; }

    public int? GiftProductId { get; set; }

    public int? GiftQuantity { get; set; }

    public string? PromotionCode { get; set; }

    public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}
