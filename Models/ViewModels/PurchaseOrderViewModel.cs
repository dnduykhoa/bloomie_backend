using System;
using System.Collections.Generic;

namespace Bloomie.Models.ViewModels
{
    public class PurchaseOrderViewModel
    {
        public int SupplierId { get; set; }
        public DateTime OrderDate { get; set; }
        public string? Note { get; set; }
        public List<PurchaseOrderDetailViewModel> Details { get; set; }
    }

    public class PurchaseOrderDetailViewModel
    {
        public int FlowerTypeId { get; set; }
        public int FlowerVariantId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
