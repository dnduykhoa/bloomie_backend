using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class CartItem
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int FlowerVariantId { get; set; }

    public int Quantity { get; set; }

    public string? Note { get; set; }

    public DateTime? DeliveryDate { get; set; }

    public string? DeliveryTime { get; set; }

    public int? ShoppingCartId { get; set; }

    public decimal? Discount { get; set; }

    public bool IsGift { get; set; }

    public virtual FlowerVariant FlowerVariant { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;

    public virtual ShoppingCart? ShoppingCart { get; set; }
}
