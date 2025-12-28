using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PromotionProduct
{
    public int PromotionId { get; set; }

    public int ProductId { get; set; }

    public int Id { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual Promotion Promotion { get; set; } = null!;
}
