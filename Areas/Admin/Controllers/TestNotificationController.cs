using Microsoft.AspNetCore.Mvc;
using Bloomie.Services;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class TestNotificationController : Controller
    {
        private readonly INotificationService _notificationService;

        public TestNotificationController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        // Test gửi thông báo đơn hàng mới
        public async Task<IActionResult> TestNewOrder()
        {
            await _notificationService.SendNotificationToAdmins(
                "🛒 Đơn hàng mới #12345 từ Nguyễn Văn A - Tổng: 250,000đ",
                "/Admin/AdminOrder/Index",
                "success"
            );
            return Content("Đã gửi thông báo đơn hàng mới!");
        }

        // Test cảnh báo hết hàng
        public async Task<IActionResult> TestLowStock()
        {
            await _notificationService.SendNotificationToAdmins(
                "⚠️ Sản phẩm 'Hoa Hồng Đỏ' chỉ còn 5 cái trong kho",
                "/Admin/AdminProduct/Index",
                "warning"
            );
            return Content("Đã gửi cảnh báo hết hàng!");
        }

        // Test đánh giá mới
        public async Task<IActionResult> TestNewRating()
        {
            await _notificationService.SendNotificationToAdmins(
                "⭐ Trần Thị B đã đánh giá 5 sao cho 'Hoa Tulip'",
                "/Admin/AdminRating/Index",
                "info"
            );
            return Content("Đã gửi thông báo đánh giá!");
        }

        // Test yêu cầu hoàn trả
        public async Task<IActionResult> TestReturnRequest()
        {
            await _notificationService.SendNotificationToAdmins(
                "🔄 Yêu cầu hoàn trả đơn hàng #12345 - Lý do: Sản phẩm bị hỏng",
                "/Admin/AdminOrder/ReturnRequests",
                "danger"
            );
            return Content("Đã gửi yêu cầu hoàn trả!");
        }
    }
}
