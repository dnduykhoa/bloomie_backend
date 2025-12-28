using System;
using System.ComponentModel.DataAnnotations;
namespace Bloomie.Models.ViewModels
{
    public class PromotionShippingDiscountVM
    {
        [Required]
        public string Code { get; set; }

        [Display(Name = "Điều kiện tối thiểu (VNĐ)")]
        public decimal? MinOrderValue { get; set; }

        // Điều kiện áp dụng nâng cao
        [Display(Name = "Loại điều kiện")]
        public string ConditionType { get; set; } // None, MinOrderValue, MinProductValue, MinProductQuantity

        [Display(Name = "Tổng giá trị sản phẩm được khuyến mãi tối thiểu")]
        public decimal? MinProductValue { get; set; }

        [Display(Name = "Tổng số lượng sản phẩm được khuyến mãi tối thiểu")]
        public int? MinProductQuantity { get; set; }

        [Display(Name = "Giá trị điều kiện")]
        public decimal? ConditionValue { get; set; }

        [Display(Name = "Số lần sử dụng tối đa")]
        public int? UsageLimit { get; set; }

        [Display(Name = "Mỗi khách hàng chỉ dùng 1 lần")]
        public bool LimitPerCustomer { get; set; }

        [Required]
        [Display(Name = "Ngày bắt đầu")]
        public DateTime StartDate { get; set; }

        [Display(Name = "Ngày kết thúc")]
        public DateTime? EndDate { get; set; }

        // Áp dụng cho tất cả hay chỉ phường/xã được chọn
        [Display(Name = "Áp dụng cho")]
        public string ApplyScope { get; set; } = "all"; // "all" hoặc "wards"
        
        // --- Các trường mới cho khuyến mãi vận chuyển nâng cao ---
        [Display(Name = "Loại khuyến mãi phí vận chuyển")]
        public string ShippingDiscountType { get; set; } // "free", "money", "percent"

        [Display(Name = "Số tiền hoặc % giảm phí vận chuyển")]
        public decimal? ShippingDiscountValue { get; set; }

        [Display(Name = "Áp dụng cho quận/huyện")]
        public List<string> ApplyDistricts { get; set; } = new List<string>();

        [Display(Name = "Bán kính áp dụng (km)")]
        public decimal? ApplyRadiusKm { get; set; }

        public bool AllowCombineOrder { get; set; }
        public bool AllowCombineProduct { get; set; }
        public bool AllowCombineShipping { get; set; }

        [Display(Name = "Kích hoạt")]
        public bool IsActive { get; set; }
        [Display(Name = "Công khai")]
        public bool IsPublic { get; set; }

    }
}