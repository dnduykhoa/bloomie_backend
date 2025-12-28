using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Bloomie.Hubs;

namespace Bloomie.Areas.Shipper.Controllers
{
    [Area("Shipper")]
    [Authorize(Roles = "Shipper")]
    public class ShipperOrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IShipperAssignmentService _shipperAssignmentService;
        private readonly IHubContext<NotificationHub> _hubContext;

        public ShipperOrderController(
            ApplicationDbContext context, 
            IShipperAssignmentService shipperAssignmentService,
            IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _shipperAssignmentService = shipperAssignmentService;
            _hubContext = hubContext;
        }

        // GET: Shipper/ShipperOrder - Danh sách đơn hàng được phân công
        public async Task<IActionResult> Index(string? statusFilter)
        {
            // Lấy UserId của shipper hiện tại
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Chỉ lấy các đơn hàng ĐƯỢC PHÂN CÔNG cho shipper này
            var query = _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .Where(o => o.ShipperId == currentUserId 
                    && (o.ShipperStatus == "Đã phân công" || o.ShipperStatus == "Đã xác nhận")
                    && (o.Status == "Đã xác nhận" || o.Status == "Đang giao"))
                .AsQueryable();

            // Lọc theo trạng thái nếu có
            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(o => o.Status == statusFilter);
            }

            var orders = await query
                .OrderBy(o => o.AssignedAt)
                .ToListAsync();

            ViewBag.StatusFilter = statusFilter;
            return View(orders);
        }

        // GET: Shipper/ShipperOrder/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            return View(order);
        }

        // GET: Shipper/ShipperOrder/History - Lịch sử giao hàng
        public async Task<IActionResult> History(DateTime? startDate, DateTime? endDate, string? statusFilter, string? paymentMethodFilter)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Mặc định: 7 ngày gần đây
            if (!startDate.HasValue || !endDate.HasValue)
            {
                endDate = DateTime.Now.Date;
                startDate = endDate.Value.AddDays(-6);
            }

            var query = _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .Where(o => o.ShipperId == currentUserId
                    && o.OrderDate.Date >= startDate.Value.Date
                    && o.OrderDate.Date <= endDate.Value.Date)
                .AsQueryable();

            // Filter theo trạng thái
            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(o => o.Status == statusFilter);
            }

            // Filter theo phương thức thanh toán
            if (!string.IsNullOrEmpty(paymentMethodFilter))
            {
                query = query.Where(o => o.PaymentMethod == paymentMethodFilter);
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");
            ViewBag.StatusFilter = statusFilter;
            ViewBag.PaymentMethodFilter = paymentMethodFilter;

            return View(orders);
        }

        // POST: Shipper/ShipperOrder/ConfirmPickup/5 - Xác nhận đã nhận hoa
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPickup(int id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Kiểm tra đơn hàng có phải của shipper này không
            if (order.ShipperId != currentUserId)
            {
                TempData["error"] = "Đơn hàng này không được phân công cho bạn.";
                return RedirectToAction("Index");
            }

            // Kiểm tra trạng thái
            if (order.ShipperStatus != "Assigned")
            {
                TempData["error"] = "Đơn hàng này đã được xác nhận hoặc không hợp lệ.";
                return RedirectToAction("Details", new { id });
            }

            // Gọi service để confirm pickup (hủy Hangfire job)
            var success = await _shipperAssignmentService.ConfirmPickupAsync(id, currentUserId!);
            
            if (success)
            {
                TempData["success"] = "Đã xác nhận nhận đơn hàng thành công! Bạn có thể bắt đầu giao hàng.";
            }
            else
            {
                TempData["error"] = "Có lỗi xảy ra khi xác nhận. Vui lòng thử lại.";
            }

            return RedirectToAction("Details", new { id });
        }

        // POST: Shipper/ShipperOrder/StartDelivery/5 - Bắt đầu giao hàng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartDelivery(int id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Kiểm tra quyền
            if (order.ShipperId != currentUserId)
            {
                TempData["error"] = "Đơn hàng này không được phân công cho bạn.";
                return RedirectToAction("Index");
            }

            // Phải confirm pickup trước mới được giao hàng
            if (order.ShipperStatus != "Confirmed")
            {
                TempData["error"] = "Bạn phải xác nhận nhận đơn hàng trước khi bắt đầu giao hàng.";
                return RedirectToAction("Details", new { id });
            }

            if (order.Status != "Đã xác nhận")
            {
                TempData["error"] = "Chỉ có thể bắt đầu giao hàng với đơn đã được xác nhận.";
                return RedirectToAction("Details", new { id });
            }

            order.Status = "Đang giao";
            await _context.SaveChangesAsync();

            // 🔔 Gửi SignalR notification cập nhật trạng thái realtime
            await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
            {
                orderStatus = order.Status,
                paymentStatus = order.PaymentStatus
            });

            TempData["success"] = "Đã bắt đầu giao hàng.";
            return RedirectToAction("Details", new { id });
        }

        // POST: Shipper/ShipperOrder/CompleteDelivery/5 - Giao hàng thành công
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteDelivery(int id, string paymentStatus, IFormFile? deliveryImage)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            if (order.Status != "Đang giao")
            {
                TempData["error"] = "Đơn hàng phải ở trạng thái 'Đang giao' mới có thể hoàn tất.";
                return RedirectToAction("Details", new { id });
            }

            // Kiểm tra ảnh bắt buộc
            if (deliveryImage == null || deliveryImage.Length == 0)
            {
                TempData["error"] = "Vui lòng chụp ảnh chứng minh giao hàng.";
                return RedirectToAction("Details", new { id });
            }

            // Xử lý upload ảnh giao hàng
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(deliveryImage.FileName).ToLowerInvariant();
            
            if (!allowedExtensions.Contains(extension))
            {
                TempData["error"] = "Chỉ chấp nhận file ảnh định dạng JPG, JPEG, PNG.";
                return RedirectToAction("Details", new { id });
            }

            if (deliveryImage.Length > 5 * 1024 * 1024) // 5MB
            {
                TempData["error"] = "Kích thước ảnh không được vượt quá 5MB.";
                return RedirectToAction("Details", new { id });
            }

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "delivery");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{order.OrderId}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await deliveryImage.CopyToAsync(fileStream);
            }

            order.DeliveryImageUrl = $"/images/delivery/{uniqueFileName}";
            order.Status = "Đã giao";
            order.DeliveryDate = DateTime.Now;

            // Cập nhật trạng thái thanh toán
            if (order.PaymentMethod == "COD")
            {
                // COD: Shipper xác nhận đã thu tiền hay chưa
                order.PaymentStatus = paymentStatus; // "Đã thanh toán" hoặc "Chưa thanh toán"
            }
            // Với Momo, VNPAY: giữ nguyên PaymentStatus đã có (đã được xác định khi thanh toán online)
            // Không cần cập nhật vì đã có sẵn: "Đã thanh toán" hoặc "Thanh toán thất bại"

            await _context.SaveChangesAsync();

            // � Cập nhật lại số đơn active của shipper (giảm đi 1)
            if (!string.IsNullOrEmpty(order.ShipperId))
            {
                await _shipperAssignmentService.UpdateShipperStatsAsync(order.ShipperId);
            }

            // �🔔 Gửi SignalR notification cập nhật trạng thái realtime
            await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
            {
                orderStatus = order.Status,
                paymentStatus = order.PaymentStatus
            });

            TempData["success"] = "Đã hoàn tất giao hàng.";
            return RedirectToAction("Index");
        }

        // POST: Shipper/ShipperOrder/FailDelivery/5 - Giao hàng thất bại
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FailDelivery(int id, string failReason)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            if (order.Status != "Đang giao")
            {
                TempData["error"] = "Đơn hàng phải ở trạng thái 'Đang giao'.";
                return RedirectToAction("Details", new { id });
            }

            // Quay lại trạng thái "Đã xác nhận" để giao lại
            order.Status = "Đã xác nhận";
            
            // Lưu lý do giao hàng thất bại vào Note
            if (!string.IsNullOrEmpty(order.Note))
            {
                order.Note += $"\n[{DateTime.Now:dd/MM/yyyy HH:mm}] Giao hàng thất bại: {failReason}";
            }
            else
            {
                order.Note = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] Giao hàng thất bại: {failReason}";
            }

            await _context.SaveChangesAsync();

            // 🔔 Gửi SignalR notification cập nhật trạng thái realtime
            await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
            {
                orderStatus = order.Status,
                paymentStatus = order.PaymentStatus
            });

            TempData["warning"] = "Đã đánh dấu giao hàng thất bại. Đơn hàng quay về trạng thái 'Đã xác nhận'.";
            return RedirectToAction("Index");
        }
    }
}
