using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public class FlowerVariant
    {
        public int Id { get; set; }

        [Required]
        public int FlowerTypeId { get; set; }

        [Required]
        public string Name { get; set; } // Tên biến thể (ví dụ: Hồng Đỏ, Cúc Vàng...)

        public FlowerType? FlowerType { get; set; } // Navigation (nullable to avoid model validation requiring it)

        [Required]
        public int Stock { get; set; } // Số lượng tồn kho cho từng biến thể
        
        [Required]
        public string Color { get; set; } // Màu sắc
        // Không bắt buộc
        
        public string Size { get; set; } // Kích thước
        
        public string Origin { get; set; } // Xuất xứ
    }
}
