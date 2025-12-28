using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class FlowerType
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int Stock { get; set; }

    public string Description { get; set; } = null!;

    public virtual ICollection<FlowerVariant> FlowerVariants { get; set; } = new List<FlowerVariant>();

    public virtual ICollection<ProductDetail> ProductDetails { get; set; } = new List<ProductDetail>();
}
