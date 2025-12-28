using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public class PurchaseOrder
    {
        public int Id { get; set; }
        public int SupplierId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Note { get; set; }
        public Supplier Supplier { get; set; }
        public ICollection<PurchaseOrderDetail>? Details { get; set; }
    }
}
