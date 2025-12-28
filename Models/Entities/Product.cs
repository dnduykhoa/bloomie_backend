namespace Bloomie.Models.Entities
{
    public class Product
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; } // ⭐ Thêm để lưu giá gốc cho Gift items
        public string? ImageUrl { get; set; }
        public int StockQuantity { get; set; }
        public bool IsActive { get; set; } = true;

        // Quan hệ với chi tiết bó hoa
        public ICollection<ProductDetail>? ProductDetails { get; set; }

        // Quan hệ với nhiều ảnh
        public ICollection<ProductImage>? Images { get; set; }

        // Thêm navigation property cho ProductCategories
        public ICollection<ProductCategory>? ProductCategories { get; set; }
    }
}