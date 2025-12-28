using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class FlowerVariant
{
    public int Id { get; set; }

    public int FlowerTypeId { get; set; }

    public string Name { get; set; } = null!;

    public int Stock { get; set; }

    public string Color { get; set; } = null!;

    public string Size { get; set; } = null!;

    public string Origin { get; set; } = null!;

    public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    public virtual FlowerType FlowerType { get; set; } = null!;

    public virtual ICollection<ProductDetail> ProductDetails { get; set; } = new List<ProductDetail>();

    public virtual ICollection<PurchaseOrderDetail> PurchaseOrderDetails { get; set; } = new List<PurchaseOrderDetail>();
}
