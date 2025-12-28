using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PromotionGift
{
    public int Id { get; set; }

    public int PromotionId { get; set; }

    public int BuyQuantity { get; set; }

    public int GiftQuantity { get; set; }

    public string BuyApplyType { get; set; } = null!;

    public string BuyConditionType { get; set; } = null!;

    public int? BuyConditionValue { get; set; }

    public decimal? BuyConditionValueMoney { get; set; }

    public string GiftApplyType { get; set; } = null!;

    public decimal? GiftDiscountMoneyValue { get; set; }

    public string GiftDiscountType { get; set; } = null!;

    public int? GiftDiscountValue { get; set; }

    public bool LimitPerOrder { get; set; }

    public virtual Promotion Promotion { get; set; } = null!;

    public virtual ICollection<PromotionGiftBuyCategory> PromotionGiftBuyCategories { get; set; } = new List<PromotionGiftBuyCategory>();

    public virtual ICollection<PromotionGiftBuyProduct> PromotionGiftBuyProducts { get; set; } = new List<PromotionGiftBuyProduct>();

    public virtual ICollection<PromotionGiftGiftCategory> PromotionGiftGiftCategories { get; set; } = new List<PromotionGiftGiftCategory>();

    public virtual ICollection<PromotionGiftGiftProduct> PromotionGiftGiftProducts { get; set; } = new List<PromotionGiftGiftProduct>();
}
