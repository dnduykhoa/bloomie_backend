using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PromotionCode
{
    public int Id { get; set; }

    public string Code { get; set; } = null!;

    public int? PromotionId { get; set; }

    public decimal? Value { get; set; }

    public int? UsageLimit { get; set; }

    public int UsedCount { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public bool IsActive { get; set; }

    public bool IsPercent { get; set; }

    public bool LimitPerCustomer { get; set; }

    public decimal? MaxDiscount { get; set; }

    public int? MinOrderValue { get; set; }

    public virtual Promotion? Promotion { get; set; }
}
