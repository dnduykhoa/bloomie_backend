using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ApiRequests
{
    /// <summary>
    /// Request khi shipper báo giao hàng thất bại
    /// </summary>
    public class FailDeliveryRequest
    {
        [Required(ErrorMessage = "Vui lòng chọn lý do giao hàng thất bại.")]
        public string Reason { get; set; } = string.Empty; // "Không liên lạc được khách", "Địa chỉ sai", "Khách từ chối nhận", "Khách hẹn giao lại"...
        
        public string? Note { get; set; } // Ghi chú chi tiết thêm từ shipper
    }

    /// <summary>
    /// Request khi shipper từ chối đơn hàng
    /// </summary>
    public class RejectOrderRequest
    {
        public string? Reason { get; set; } // Lý do từ chối (optional)
    }
}
