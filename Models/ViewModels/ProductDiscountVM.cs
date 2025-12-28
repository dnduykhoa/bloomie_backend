using System.ComponentModel.DataAnnotations;
using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class ProductDiscountVM
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên chương trình giảm giá")]
        [Display(Name = "Tên chương trình")]
        public string Name { get; set; }

        [Display(Name = "Mô tả")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn loại giảm giá")]
        [Display(Name = "Loại giảm giá")]
        public string DiscountType { get; set; } // "percent" hoặc "fixed_amount"

        [Required(ErrorMessage = "Vui lòng nhập giá trị giảm")]
        [Display(Name = "Giá trị giảm")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Giá trị giảm phải lớn hơn 0")]
        public decimal DiscountValue { get; set; }

        [Display(Name = "Giảm tối đa (VNĐ)")]
        [Range(0, double.MaxValue, ErrorMessage = "Giảm tối đa phải >= 0")]
        public decimal? MaxDiscount { get; set; }

        [Display(Name = "Ngày bắt đầu")]
        public DateTime? StartDate { get; set; }

        [Display(Name = "Ngày kết thúc")]
        public DateTime? EndDate { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn phạm vi áp dụng")]
        [Display(Name = "Áp dụng cho")]
        public string ApplyTo { get; set; } // "products", "categories", "all"

        [Display(Name = "Danh sách sản phẩm")]
        public List<int>? SelectedProductIds { get; set; }

        [Display(Name = "Danh sách danh mục")]
        public List<int>? SelectedCategoryIds { get; set; }

        [Display(Name = "Độ ưu tiên")]
        [Range(1, 100, ErrorMessage = "Độ ưu tiên từ 1-100")]
        public int Priority { get; set; } = 1;

        [Display(Name = "Trạng thái")]
        public bool IsActive { get; set; } = true;

        // Cho dropdown
        public List<Product>? AvailableProducts { get; set; }
        public List<Category>? AvailableCategories { get; set; }
    }
}
