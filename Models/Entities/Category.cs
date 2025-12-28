using System.Collections.Generic;

namespace Bloomie.Models.Entities
{
    public enum CategoryType
    {
        Topic = 0,
        Recipient = 1,
        Shape = 2, // Kiểu dáng
        // Có thể mở rộng thêm: FreshFlower = 4, Color = 5, ...
    }

    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Type { get; set; } // Sử dụng int để Razor binding dễ dàng
        public string? Description { get; set; }
        public int? ParentId { get; set; }
        public Category? Parent { get; set; }
        public ICollection<Category>? Children { get; set; }
        public ICollection<ProductCategory>? ProductCategories { get; set; }
    }
}
