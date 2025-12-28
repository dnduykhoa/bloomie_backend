using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class ProductCategory
{
    public int ProductId { get; set; }

    public int CategoryId { get; set; }

    public int Id { get; set; }

    public virtual Category Category { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
