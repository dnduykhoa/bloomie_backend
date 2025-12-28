using System;
using System.Collections.Generic;

namespace Bloomie.Models.Entities
{
    public class Order
    {
        public int Id { get; set; }
        public string? OrderId { get; set; } // Mã đơn hàng ngẫu nhiên dạng 2511045SJN
        public string? UserId { get; set; } // Nếu có quản lý người dùng
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Status { get; set; } // Chờ xác nhận, Đang gói, Đang giao, Hoàn thành...
        public string? PaymentStatus { get; set; } // Chưa thanh toán, Đã thanh toán
        public string? PaymentMethod { get; set; } // Momo, VNPAY, COD...
        public string? ReceiverName { get; set; } // Tên người nhận hàng
        public string? ShippingAddress { get; set; }
        public string? Phone { get; set; }
        public string? Note { get; set; }
        public string? CancellationJobId { get; set; } // Lưu Hangfire JobId để hủy job khi thanh toán thành công
        public DateTime? DeliveryDate { get; set; } // Ngày giao hàng thực tế
        public string? DeliveryImageUrl { get; set; } // Ảnh chứng minh giao hàng
        public DateTime? DeliveryImageUploadedAt { get; set; } // Thời gian chụp ảnh bằng chứng
        public int PointsUsed { get; set; } = 0; // Số điểm đã sử dụng cho đơn hàng này
        
        // Thông tin giảm giá
        public decimal PromotionDiscount { get; set; } = 0; // Giảm giá từ promotion tự động
        public decimal VoucherDiscount { get; set; } = 0; // Giảm giá từ voucher đơn hàng/sản phẩm
        public decimal ShippingDiscount { get; set; } = 0; // Giảm giá từ voucher vận chuyển
        public decimal PointsDiscount { get; set; } = 0; // Giảm giá từ điểm tích lũy
        public decimal ShippingFee { get; set; } = 0; // Phí vận chuyển
        public string? DiscountVoucherCode { get; set; } // Mã voucher giảm giá đã sử dụng
        public string? ShippingVoucherCode { get; set; } // Mã voucher vận chuyển đã sử dụng
        
        // Thông tin phân công shipper
        public string? ShipperId { get; set; } // ID của shipper được phân công
        public DateTime? AssignedAt { get; set; } // Thời điểm phân công cho shipper
        public DateTime? ShipperConfirmedAt { get; set; } // Thời điểm shipper xác nhận đã nhận hoa
        public string? ShipperStatus { get; set; } // Assigned, Confirmed, Rejected
        public string? ReassignmentJobId { get; set; } // Hangfire job ID để tự động chuyển đơn nếu shipper không confirm
        
        // GPS Tracking
        public double? LastKnownLatitude { get; set; } // Vĩ độ cuối cùng của shipper
        public double? LastKnownLongitude { get; set; } // Kinh độ cuối cùng của shipper
        public DateTime? LastGPSUpdate { get; set; } // Thời gian cập nhật GPS cuối cùng
        
        // Thông tin hủy đơn
        public string? CancelReason { get; set; } // Lý do hủy đơn hàng từ khách hàng
        public DateTime? CancelledAt { get; set; } // Thời gian hủy đơn hàng
        
        public ICollection<OrderDetail>? OrderDetails { get; set; }
    }
}
