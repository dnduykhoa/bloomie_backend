using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class PromotionGiftVM
    {
        public int Id { get; set; } // Thêm Id để lưu trữ khóa chính khi chỉnh sửa

        [Required(ErrorMessage = "Mã chương trình là bắt buộc")]
        public string Code { get; set; }


        // Điều kiện mua
        [Required(ErrorMessage = "Loại điều kiện mua là bắt buộc")]
        public string BuyConditionType { get; set; } // MinQuantity hoặc MinValue

        public int? BuyConditionValue { get; set; } // Số lượng tối thiểu

        public decimal? BuyConditionValueMoney { get; set; } // Giá trị tối thiểu (VNĐ)

        [Required(ErrorMessage = "Loại áp dụng là bắt buộc")]
        public string BuyApplyType { get; set; } // product hoặc category

        public List<int> BuyProductIds { get; set; } = new List<int>(); // Danh sách ID sản phẩm mua

        public List<int> BuyCategoryIds { get; set; } = new List<int>(); // Danh sách ID danh mục mua

        // Sản phẩm tặng
        [Required(ErrorMessage = "Số lượng tặng là bắt buộc")]
        public int GiftQuantity { get; set; }

        [Required(ErrorMessage = "Loại áp dụng tặng là bắt buộc")]
        public string GiftApplyType { get; set; } // product hoặc category

        public List<int> GiftProductIds { get; set; } = new List<int>(); // Danh sách ID sản phẩm tặng

        public List<int> GiftCategoryIds { get; set; } = new List<int>(); // Danh sách ID danh mục tặng

        [Required(ErrorMessage = "Loại giảm giá là bắt buộc")]
        public string GiftDiscountType { get; set; } // percent, money, hoặc free

        public int? GiftDiscountValue { get; set; } // Phần trăm giảm (0-100)

        public decimal? GiftDiscountMoneyValue { get; set; } // Giá trị tiền giảm (VNĐ)

        [Display(Name = "Số lần sử dụng tối đa")]
        public int? UsageLimit { get; set; }

        [Display(Name = "Giới hạn mỗi khách hàng")]
        public bool LimitPerCustomer { get; set; }

        [Display(Name = "Giới hạn số lần áp dụng tối đa trong đơn")]
        public bool LimitPerOrder { get; set; }

        // Kết hợp khuyến mãi
        [Display(Name = "Kết hợp giảm giá đơn hàng")]
        public bool AllowCombineOrder { get; set; }

        [Display(Name = "Kết hợp giảm giá sản phẩm")]
        public bool AllowCombineProduct { get; set; }

        [Display(Name = "Kết hợp miễn phí vận chuyển")]
        public bool AllowCombineShipping { get; set; }

        // Thời gian
        [Required(ErrorMessage = "Ngày bắt đầu là bắt buộc")]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }
        
        [Display(Name = "Kích hoạt")]
        public bool IsActive { get; set; }
        [Display(Name = "Công khai")]
        public bool IsPublic { get; set; }
    }
}