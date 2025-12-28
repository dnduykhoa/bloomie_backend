using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public class ProductDetail
    {
        public int Id { get; set; }
        public int ProductId { get; set; } // Bó hoa
    public int FlowerVariantId { get; set; } // Biến thể hoa (màu sắc, kiểu dáng...)
    public int Quantity { get; set; } // Số lượng cành hoa

    public Product Product { get; set; }
    public FlowerVariant FlowerVariant { get; set; }
    }
}
