namespace Bloomie.Models.Entities
{
    public class PurchaseOrderDetail
    {
        public int Id { get; set; }
        public int PurchaseOrderId { get; set; }
    public int FlowerVariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; }
    public FlowerVariant FlowerVariant { get; set; }
    }
}
