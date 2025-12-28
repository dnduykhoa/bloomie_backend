using System;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class PromotionOrderDiscountVM
    {
        [Required]
        [Display(Name = "Mã khuyến mãi")]
        public string Code { get; set; }

        [Required]
        [Display(Name = "Giá trị khuyến mãi")]
        public decimal Value { get; set; }

        [Display(Name = "% hay số tiền")]
        public bool IsPercent { get; set; } = false;

        [Display(Name = "Giá trị giảm tối đa")]
        public decimal? MaxDiscount { get; set; }

        [Display(Name = "Loại điều kiện")]
        public string ConditionType { get; set; } = "None"; // None, MinOrderValue, MinProductQuantity

        [Display(Name = "Giá trị điều kiện")]
        public decimal? ConditionValue { get; set; }

        // Lưu riêng từng giá trị điều kiện để UX tốt hơn khi chuyển radio
        [Display(Name = "Tổng giá trị đơn hàng tối thiểu")]
        public decimal? MinOrderValue { get; set; }

        [Display(Name = "Tổng số lượng sản phẩm được khuyến mãi tối thiểu")]
        public int? MinProductQuantity { get; set; }

        [Display(Name = "Số lần sử dụng tối đa")]
        public int? UsageLimit { get; set; }

    [Display(Name = "Giới hạn mỗi khách hàng")]
    public bool LimitPerCustomer { get; set; }


        [Display(Name = "Kết hợp giảm giá đơn hàng")]
        public bool AllowCombineOrder { get; set; }

        [Display(Name = "Kết hợp giảm giá sản phẩm")]
        public bool AllowCombineProduct { get; set; }

        [Display(Name = "Kết hợp miễn phí vận chuyển")]
        public bool AllowCombineShipping { get; set; }

        [Required]
        [Display(Name = "Ngày bắt đầu")]
        public DateTime StartDate { get; set; }

        [Display(Name = "Ngày kết thúc")]
        public DateTime? EndDate { get; set; }

        [Display(Name = "Kích hoạt")]
        public bool IsActive { get; set; }

        [Display(Name = "Công khai")]
        public bool IsPublic { get; set; }
    }
}
