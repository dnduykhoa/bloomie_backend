using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PurchaseOrderDetail
{
    public int Id { get; set; }

    public int PurchaseOrderId { get; set; }

    public int FlowerVariantId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public virtual FlowerVariant FlowerVariant { get; set; } = null!;

    public virtual PurchaseOrder PurchaseOrder { get; set; } = null!;
}
