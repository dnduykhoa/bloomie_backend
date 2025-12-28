using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PromotionOrder
{
    public int PromotionId { get; set; }

    public int OrderId { get; set; }

    public int Id { get; set; }

    public virtual Promotion Promotion { get; set; } = null!;
}
