using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class PromotionProductDiscountVM
    {
        [Required]
        public string? Code { get; set; }
        [Required]
        public decimal Value { get; set; }
        public bool IsPercent { get; set; }
        public decimal? MaxDiscount { get; set; }
        public List<int>? ProductIds { get; set; }
        public List<int>? CategoryIds { get; set; } // Danh mục áp dụng
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        [Display(Name = "Số lần sử dụng tối đa")]
        public int? UsageLimit { get; set; }
        public string? ConditionType { get; set; }
        public decimal? ConditionValue { get; set; }
        public bool AllowCombineOrder { get; set; }
        public bool AllowCombineProduct { get; set; }
        public bool AllowCombineShipping { get; set; }

        // Thêm các property điều kiện riêng để UX tốt hơn
        public decimal? MinOrderValue { get; set; }
        public decimal? MinProductValue { get; set; }
        public int? MinProductQuantity { get; set; }
        public bool LimitPerCustomer { get; set; }
        
        [Display(Name = "Kích hoạt")]
        public bool IsActive { get; set; }
        [Display(Name = "Công khai")]
        public bool IsPublic { get; set; }
    }
}
