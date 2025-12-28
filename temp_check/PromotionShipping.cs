using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PromotionShipping
{
    public int Id { get; set; }

    public int PromotionId { get; set; }

    public decimal? ShippingDiscount { get; set; }

    public virtual Promotion Promotion { get; set; } = null!;
}
