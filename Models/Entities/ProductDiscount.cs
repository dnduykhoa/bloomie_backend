using System;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    /// <summary>
    /// Giảm giá sản phẩm trực tiếp (không cần mã code)
    /// Khác với Voucher/PromotionCode (cần nhập mã)
    /// </summary>
    public class ProductDiscount
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty; // "Black Friday Sale", "Flash Sale 12h"

        public string? Description { get; set; }

        // Loại giảm giá
        [Required]
        [MaxLength(20)]
        public string DiscountType { get; set; } = "percent"; // "percent" hoặc "fixed_amount"

        [Required]
        public decimal DiscountValue { get; set; } // 20 (cho 20%) hoặc 100000 (cho 100k)

        public decimal? MaxDiscount { get; set; } // Giảm tối đa (cho % discount)

        // Thời gian áp dụng
        [Required]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; } // Null = vô thời hạn

        // Áp dụng cho sản phẩm/danh mục nào?
        [MaxLength(20)]
        public string ApplyTo { get; set; } = "products"; // "products", "categories", "all"

        public string? ProductIds { get; set; } // JSON: [1,2,3,4] - Danh sách ID sản phẩm
        public string? CategoryIds { get; set; } // JSON: [1,2,3] - Danh sách ID danh mục

        // Ưu tiên (nếu nhiều discount cho 1 sản phẩm)
        public int Priority { get; set; } = 0; // 0 = thấp nhất

        // Trạng thái
        public bool IsActive { get; set; } = true;

        // Metadata
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? UpdatedDate { get; set; }

        // Tracking
        public int ViewCount { get; set; } = 0; // Số lượt xem
        public int PurchaseCount { get; set; } = 0; // Số lượt mua
        public decimal TotalRevenue { get; set; } = 0; // Doanh thu từ discount này
    }
}
