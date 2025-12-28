using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class ProductFlowerDetailVM
    {
        public int FlowerVariantId { get; set; }
        public int FlowerTypeId { get; set; } // Thêm để JS render lại đúng
        public int Quantity { get; set; }
    }

    public class ProductViewModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public int? ShapeCategoryId { get; set; } // Kiểu trình bày
        public string? Description { get; set; }
        public List<int> CategoryIds { get; set; } = new(); // Danh mục sản phẩm
        public List<ProductFlowerDetailVM> Flowers { get; set; } = new(); // Loại hoa và số lượng
        [Display(Name = "Ảnh chính")]
        public IFormFile? MainImage { get; set; } // Ảnh chính
        public string? ExistingMainImageUrl { get; set; }
        
        public List<IFormFile>? SubImages { get; set; } // Ảnh phụ - nullable và không required

        // Dùng cho Edit
        public List<string> ExistingSubImageUrls { get; set; } = new();
    }
}
