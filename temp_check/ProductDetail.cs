using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class ProductDetail
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int FlowerVariantId { get; set; }

    public int Quantity { get; set; }

    public int? FlowerTypeId { get; set; }

    public virtual FlowerType? FlowerType { get; set; }

    public virtual FlowerVariant FlowerVariant { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
