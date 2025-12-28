using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public class FlowerType
    {
        public int Id { get; set; }
        [Required]
        public string? Name { get; set; }
        public int Stock { get; set; } // Số lượng tồn kho (cành)
        public string Description { get; set; }
        public ICollection<ProductDetail>? ProductDetails { get; set; }
    }
}
