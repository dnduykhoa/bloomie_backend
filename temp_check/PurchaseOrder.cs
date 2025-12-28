using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class PurchaseOrder
{
    public int Id { get; set; }

    public int SupplierId { get; set; }

    public DateTime OrderDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Note { get; set; }

    public virtual ICollection<PurchaseOrderDetail> PurchaseOrderDetails { get; set; } = new List<PurchaseOrderDetail>();

    public virtual Supplier Supplier { get; set; } = null!;
}
