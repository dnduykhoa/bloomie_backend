using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Controllers
{
    [Authorize]
    public class ServiceReviewController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;

        public ServiceReviewController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        [HttpPost]
        [Route("/ServiceReview")]
        public async Task<IActionResult> Index(int orderId, int deliveryRating, int serviceRating, string? comment)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập để đánh giá!" });
                }

                // Kiểm tra đơn hàng có tồn tại và thuộc về user không
                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

                if (order == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng!" });
                }

                // Kiểm tra đơn hàng đã hoàn thành chưa
                if (order.Status != "Hoàn thành")
                {
                    return Json(new { success = false, message = "Chỉ có thể đánh giá đơn hàng đã hoàn thành!" });
                }

                // Kiểm tra đã đánh giá chưa
                var existingReview = await _context.ServiceReviews
                    .FirstOrDefaultAsync(r => r.OrderId == orderId && r.UserId == userId);

                if (existingReview != null)
                {
                    return Json(new { success = false, message = "Bạn đã đánh giá đơn hàng này rồi!" });
                }

                // Tạo đánh giá mới
                var review = new ServiceReview
                {
                    OrderId = orderId,
                    UserId = userId,
                    DeliveryRating = deliveryRating,
                    ServiceRating = serviceRating,
                    OverallRating = (int)Math.Round((deliveryRating + serviceRating) / 2.0),
                    Comment = comment,
                    CreatedAt = DateTime.Now
                };

                _context.ServiceReviews.Add(review);
                await _context.SaveChangesAsync();

                // 🔔 GỬI THÔNG BÁO REALTIME CHO ADMIN
                try
                {
                    var user = await _userManager.GetUserAsync(User);
                    var customerName = user?.FullName ?? "Khách hàng";
                    var stars = string.Concat(Enumerable.Repeat("⭐", review.OverallRating));
                    await _notificationService.SendNotificationToAdmins(
                        $"{stars} {customerName} đánh giá dịch vụ đơn #{order?.OrderId ?? orderId.ToString()} - {review.OverallRating}/5 sao",
                        "/Admin/AdminRating/Index",
                        "info"
                    );
                }
                catch { }

                return Json(new { success = true, message = "Cảm ơn bạn đã đánh giá!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
            }
        }
    }
}
