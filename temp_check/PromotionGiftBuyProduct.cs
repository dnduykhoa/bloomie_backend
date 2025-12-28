using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PromotionGiftBuyProduct
{
    public int Id { get; set; }

    public int PromotionGiftId { get; set; }

    public int ProductId { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual PromotionGift PromotionGift { get; set; } = null!;
}
