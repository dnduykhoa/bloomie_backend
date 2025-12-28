using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PromotionGiftBuyCategory
{
    public int Id { get; set; }

    public int PromotionGiftId { get; set; }

    public int CategoryId { get; set; }

    public virtual Category Category { get; set; } = null!;

    public virtual PromotionGift PromotionGift { get; set; } = null!;
}
