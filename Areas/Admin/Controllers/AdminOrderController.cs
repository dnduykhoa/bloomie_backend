using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Bloomie.Services;
using Microsoft.AspNetCore.SignalR;
using Bloomie.Hubs;
using Microsoft.AspNetCore.Identity;
using Hangfire;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin,Staff")]
    public class AdminOrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly IShipperAssignmentService _shipperAssignmentService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminOrderController(
            ApplicationDbContext context, 
            IEmailService emailService, 
            INotificationService notificationService, 
            IShipperAssignmentService shipperAssignmentService,
            IHubContext<NotificationHub> hubContext,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _emailService = emailService;
            _notificationService = notificationService;
            _shipperAssignmentService = shipperAssignmentService;
            _hubContext = hubContext;
            _userManager = userManager;
        }

        // GET: Admin/AdminOrder
        public async Task<IActionResult> Index(string? statusFilter, string? paymentStatusFilter, string? deliveryTypeFilter, DateTime? fromDate, DateTime? toDate)
        {
            var query = _context.Orders
                .Include(o => o.OrderDetails)
                .AsQueryable();

            // Lọc theo trạng thái đơn hàng
            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(o => o.Status == statusFilter);
            }

            // Lọc theo trạng thái thanh toán
            if (!string.IsNullOrEmpty(paymentStatusFilter))
            {
                query = query.Where(o => o.PaymentStatus == paymentStatusFilter);
            }

            // ⭐ Lọc theo loại đơn hàng (đơn đặt trước hay giao hôm nay)
            if (!string.IsNullOrEmpty(deliveryTypeFilter))
            {
                var today = DateTime.Today;
                if (deliveryTypeFilter == "PreOrder")
                {
                    // Đơn đặt trước: có ít nhất 1 sản phẩm giao sau hôm nay
                    query = query.Where(o => o.OrderDetails!.Any(d => d.DeliveryDate != null && d.DeliveryDate.Value.Date > today));
                }
                else if (deliveryTypeFilter == "Today")
                {
                    // Giao hôm nay: tất cả sản phẩm giao hôm nay hoặc không có ngày giao
                    query = query.Where(o => !o.OrderDetails!.Any(d => d.DeliveryDate != null && d.DeliveryDate.Value.Date > today));
                }
            }

            // Lọc theo ngày
            if (fromDate.HasValue)
            {
                query = query.Where(o => o.OrderDate.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(o => o.OrderDate.Date <= toDate.Value.Date);
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            // Tính toán thống kê
            var allOrders = await _context.Orders.ToListAsync();
            ViewBag.TotalOrders = allOrders.Count;
            ViewBag.PendingOrders = allOrders.Count(o => o.Status == "Chờ xác nhận");
            ViewBag.CompletedOrders = allOrders.Count(o => o.Status == "Hoàn thành");
            ViewBag.TotalRevenue = allOrders.Where(o => o.Status == "Hoàn thành").Sum(o => o.TotalAmount);

            ViewBag.StatusFilter = statusFilter;
            ViewBag.PaymentStatusFilter = paymentStatusFilter;
            ViewBag.DeliveryTypeFilter = deliveryTypeFilter;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;

            return View(orders);
        }

        // GET: Admin/AdminOrder/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        // POST: Admin/AdminOrder/ConfirmOrder/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmOrder(int id)
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

            // Chỉ cho phép xác nhận đơn hàng ở trạng thái "Chờ xác nhận"
            if (order.Status != "Chờ xác nhận")
            {
                TempData["error"] = "Không thể xác nhận đơn hàng ở trạng thái hiện tại.";
                return RedirectToAction("Details", new { id });
            }

            // Kiểm tra và trừ số lượng tồn kho
            if (order.OrderDetails != null)
            {
                foreach (var detail in order.OrderDetails)
                {
                    var product = detail.Product;
                    if (product == null) continue;

                    // Kiểm tra số lượng tồn kho
                    if (product.StockQuantity < detail.Quantity)
                    {
                        TempData["error"] = $"Sản phẩm '{product.Name}' không đủ số lượng trong kho (Còn: {product.StockQuantity}, Cần: {detail.Quantity}).";
                        return RedirectToAction("Details", new { id });
                    }

                    // Trừ số lượng tồn kho sản phẩm
                    product.StockQuantity -= detail.Quantity;

                    // 🔔 CẢNH BÁO SẢN PHẨM SẮP HẾT HÀNG
                    try
                    {
                        if (product.StockQuantity <= 10 && product.StockQuantity > 0)
                        {
                            await _notificationService.SendNotificationToAdmins(
                                $"⚠️ Cảnh báo: Sản phẩm '{product.Name}' chỉ còn {product.StockQuantity} cái trong kho!",
                                $"/Admin/AdminProduct/Edit/{product.Id}",
                                "warning"
                            );
                        }
                        else if (product.StockQuantity <= 0)
                        {
                            await _notificationService.SendNotificationToAdmins(
                                $"🚨 KHẨN: Sản phẩm '{product.Name}' đã HẾT HÀNG!",
                                $"/Admin/AdminProduct/Edit/{product.Id}",
                                "danger"
                            );
                        }
                    }
                    catch { }

                    // Trừ kho nguyên liệu (biến thể hoa) dựa vào ProductDetail
                    var productDetails = await _context.ProductDetails
                        .Where(pd => pd.ProductId == product.Id)
                        .ToListAsync();

                    foreach (var pd in productDetails)
                    {
                        var flowerVariant = await _context.FlowerVariants.FindAsync(pd.FlowerVariantId);
                        if (flowerVariant != null)
                        {
                            // Kiểm tra số lượng nguyên liệu
                            int requiredQuantity = pd.Quantity * detail.Quantity;
                            if (flowerVariant.Stock < requiredQuantity)
                            {
                                TempData["error"] = $"Nguyên liệu '{flowerVariant.Name}' không đủ số lượng (Còn: {flowerVariant.Stock}, Cần: {requiredQuantity}).";
                                return RedirectToAction("Details", new { id });
                            }
                            // Trừ kho nguyên liệu
                            flowerVariant.Stock -= requiredQuantity;

                            // 🔔 CẢNH BÁO NGUYÊN LIỆU SẮP HẾT
                            try
                            {
                                if (flowerVariant.Stock <= 50 && flowerVariant.Stock > 0)
                                {
                                    await _notificationService.SendNotificationToAdmins(
                                        $"⚠️ Nguyên liệu '{flowerVariant.Name}' còn {flowerVariant.Stock} - cần nhập thêm!",
                                        "/Admin/AdminFlowerVariant/Index",
                                        "warning"
                                    );
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            // Cập nhật trạng thái đơn hàng
            order.Status = "Đã xác nhận";
            await _context.SaveChangesAsync();

            // 🔔 Gửi SignalR notification cập nhật trạng thái realtime
            await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
            {
                orderStatus = order.Status,
                paymentStatus = order.PaymentStatus
            });

            // ⭐ KIỂM TRA ĐƠN ĐẶT TRƯỚC - KHÔNG TỰ ĐỘNG PHÂN CÔNG SHIPPER
            var earliestDelivery = order.OrderDetails?
                .Where(d => d.DeliveryDate != null)
                .OrderBy(d => d.DeliveryDate)
                .FirstOrDefault();
            
            bool isPreOrder = earliestDelivery != null && earliestDelivery.DeliveryDate!.Value.Date > DateTime.Today;
            
            if (isPreOrder)
            {
                // Đơn đặt trước - KHÔNG phân công shipper ngay
                var deliveryDateStr = earliestDelivery!.DeliveryDate!.Value.ToString("dd/MM/yyyy");
                var deliveryTimeStr = earliestDelivery.DeliveryTime ?? "chưa xác định";
                
                TempData["success"] = $"Đã xác nhận đơn hàng ĐẶT TRƯỚC. Ngày giao: {deliveryDateStr}, {deliveryTimeStr}. " +
                                     $"Hệ thống sẽ TỰ ĐỘNG phân công shipper vào lúc 06:00 sáng ngày giao.";
                
                // Gửi thông báo nhắc nhở cho admin
                try
                {
                    await _notificationService.SendNotificationToAdmins(
                        $"📦 Đơn hàng #{order.OrderId} ĐẶT TRƯỚC - Giao {deliveryDateStr} {deliveryTimeStr}. Hệ thống sẽ tự động phân công shipper vào 06:00 sáng ngày giao.",
                        $"/Admin/AdminOrder/Details/{order.Id}",
                        "info"
                    );
                }
                catch { }
            }
            else
            {
                // Đơn giao hôm nay - TỰ ĐỘNG phân công shipper (Round Robin)
                var assignmentSuccess = await _shipperAssignmentService.AssignOrderToShipperAsync(order.Id);
                
                if (assignmentSuccess)
                {
                    // Lấy thông tin shipper được phân công
                    var assignedOrder = await _context.Orders
                        .FirstOrDefaultAsync(o => o.Id == order.Id);
                    
                    if (assignedOrder?.ShipperId != null)
                    {
                        var shipper = await _context.Users.FindAsync(assignedOrder.ShipperId);
                        TempData["success"] = $"Đã xác nhận đơn hàng. Hệ thống đã TỰ ĐỘNG phân công cho shipper: {shipper?.FullName ?? "N/A"}. " +
                                             $"Shipper có 3 phút để xác nhận nhận đơn.";
                    }
                    else
                    {
                        TempData["success"] = "Đã xác nhận đơn hàng. Hệ thống đã tự động phân công shipper thành công.";
                    }
                }
                else
                {
                    TempData["warning"] = "⚠️ Đã xác nhận đơn hàng nhưng không có shipper khả dụng. Vui lòng kiểm tra trạng thái shipper.";
                }
            }

            // Gửi email thông báo cho khách hàng
            await SendOrderConfirmedEmailAsync(order);

            return RedirectToAction("Details", new { id });
        }

        // POST: Admin/AdminOrder/CancelOrder/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelOrder(int id, string? cancelReason)
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

            // Cho phép hủy đơn hàng ở trạng thái "Chờ xác nhận" hoặc "Đã xác nhận"
            if (order.Status != "Chờ xác nhận" && order.Status != "Đã xác nhận")
            {
                TempData["error"] = "Không thể hủy đơn hàng ở trạng thái hiện tại.";
                return RedirectToAction("Details", new { id });
            }

            // Nếu đơn hàng đã được xác nhận, cần hoàn lại số lượng tồn kho
            if (order.Status == "Đã xác nhận" && order.OrderDetails != null)
            {
                foreach (var detail in order.OrderDetails)
                {
                    var product = detail.Product;
                    if (product == null) continue;

                    // Hoàn lại số lượng tồn kho sản phẩm
                    product.StockQuantity += detail.Quantity;

                    // Hoàn lại kho nguyên liệu (biến thể hoa)
                    var productDetails = await _context.ProductDetails
                        .Where(pd => pd.ProductId == product.Id)
                        .ToListAsync();

                    foreach (var pd in productDetails)
                    {
                        var flowerVariant = await _context.FlowerVariants.FindAsync(pd.FlowerVariantId);
                        if (flowerVariant != null)
                        {
                            // Hoàn lại kho nguyên liệu
                            flowerVariant.Stock += pd.Quantity * detail.Quantity;
                        }
                    }
                }
            }

            // Cập nhật trạng thái đơn hàng
            order.Status = "Đã hủy";
            await _context.SaveChangesAsync();

            // 🔔 Gửi SignalR notification cập nhật trạng thái realtime
            await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
            {
                orderStatus = order.Status,
                paymentStatus = order.PaymentStatus
            });

            // Gửi email thông báo hủy đơn hàng cho khách hàng
            await SendOrderCancelledEmailAsync(order, cancelReason);

            TempData["success"] = "Đã hủy đơn hàng. Hệ thống đã tự động hoàn kho sản phẩm và nguyên liệu.";
            return RedirectToAction("Details", new { id });
        }

        // POST: Admin/AdminOrder/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, string newStatus)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Validate trạng thái hợp lệ

            var validStatuses = new[] { "Chờ xác nhận", "Đã xác nhận", "Đang giao", "Đã giao", "Hoàn thành", "Đã hủy" };
            if (!validStatuses.Contains(newStatus))
            {
                TempData["error"] = "Trạng thái không hợp lệ.";
                return RedirectToAction("Details", new { id });
            }

            order.Status = newStatus;
            await _context.SaveChangesAsync();

            // � Cập nhật lại số đơn active của shipper nếu đơn đã hoàn thành/hủy
            if (!string.IsNullOrEmpty(order.ShipperId) && 
                (newStatus == "Đã giao" || newStatus == "Hoàn thành" || newStatus == "Đã hủy"))
            {
                await _shipperAssignmentService.UpdateShipperStatsAsync(order.ShipperId);
            }

            // �🔔 Gửi SignalR notification cập nhật trạng thái realtime
            await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
            {
                orderStatus = order.Status,
                paymentStatus = order.PaymentStatus
            });

            // Gửi email khi chuyển sang "Đã giao"
            if (newStatus == "Đã giao")
            {
                await SendOrderDeliveredEmailAsync(order);
            }

            TempData["success"] = $"Đã cập nhật trạng thái đơn hàng thành '{newStatus}'.";
            return RedirectToAction("Details", new { id });
        }

        // GET: Admin/AdminOrder/ReturnRequests - Danh sách yêu cầu đổi trả
        public async Task<IActionResult> ReturnRequests(string? statusFilter, string? typeFilter, DateTime? fromDate, DateTime? toDate)
        {
            var query = _context.OrderReturns
                .Include(r => r.Order)
                .AsQueryable();

            // Lọc theo trạng thái
            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(r => r.Status == statusFilter);
            }

            // Lọc theo loại yêu cầu
            if (!string.IsNullOrEmpty(typeFilter))
            {
                query = query.Where(r => r.ReturnType == typeFilter);
            }

            // Lọc theo ngày
            if (fromDate.HasValue)
            {
                query = query.Where(r => r.RequestDate.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(r => r.RequestDate.Date <= toDate.Value.Date);
            }

            var returns = await query
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            ViewBag.StatusFilter = statusFilter;
            ViewBag.TypeFilter = typeFilter;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            return View(returns);
        }

        // GET: Admin/AdminOrder/ReturnDetails/5 - Chi tiết yêu cầu đổi trả
        public async Task<IActionResult> ReturnDetails(int id)
        {
            var returnRequest = await _context.OrderReturns
                .Include(r => r.Order)
                    .ThenInclude(o => o!.OrderDetails!)
                        .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (returnRequest == null)
            {
                TempData["error"] = "Không tìm thấy yêu cầu đổi trả.";
                return RedirectToAction("ReturnRequests");
            }

            return View(returnRequest);
        }

        // POST: Admin/AdminOrder/ApproveReturn/5 - Chấp nhận đổi trả
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveReturn(int id, string? adminNote, decimal? refundAmount)
        {
            var returnRequest = await _context.OrderReturns
                .Include(r => r.Order)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (returnRequest == null)
            {
                TempData["error"] = "Không tìm thấy yêu cầu đổi trả.";
                return RedirectToAction("ReturnRequests");
            }

            if (returnRequest.Status != "Chờ xử lý")
            {
                TempData["error"] = "Yêu cầu này đã được xử lý.";
                return RedirectToAction("ReturnDetails", new { id });
            }

            returnRequest.Status = returnRequest.ReturnType == "Hoàn tiền" ? "Chấp nhận" : "Đã hoàn tiền";
            returnRequest.ResponseDate = DateTime.Now;
            returnRequest.AdminNote = adminNote;
            returnRequest.RefundAmount = refundAmount ?? returnRequest.Order!.TotalAmount;

            await _context.SaveChangesAsync();

            // Gửi email thông báo cho khách hàng
            await SendReturnApprovedEmailAsync(returnRequest);

            TempData["success"] = "Đã chấp nhận yêu cầu đổi trả.";
            return RedirectToAction("ReturnDetails", new { id });
        }

        // POST: Admin/AdminOrder/RejectReturn/5 - Từ chối đổi trả
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectReturn(int id, string? adminNote)
        {
            var returnRequest = await _context.OrderReturns
                .Include(r => r.Order)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (returnRequest == null)
            {
                TempData["error"] = "Không tìm thấy yêu cầu đổi trả.";
                return RedirectToAction("ReturnRequests");
            }

            if (returnRequest.Status != "Chờ xử lý")
            {
                TempData["error"] = "Yêu cầu này đã được xử lý.";
                return RedirectToAction("ReturnDetails", new { id });
            }

            returnRequest.Status = "Từ chối";
            returnRequest.ResponseDate = DateTime.Now;
            returnRequest.AdminNote = adminNote;

            await _context.SaveChangesAsync();

            // Gửi email thông báo cho khách hàng
            await SendReturnRejectedEmailAsync(returnRequest);

            TempData["success"] = "Đã từ chối yêu cầu đổi trả.";
            return RedirectToAction("ReturnDetails", new { id });
        }

        // POST: Admin/AdminOrder/CompleteRefund/5 - Hoàn tất hoàn tiền
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteRefund(int id)
        {
            var returnRequest = await _context.OrderReturns
                .Include(r => r.Order)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (returnRequest == null)
            {
                TempData["error"] = "Không tìm thấy yêu cầu đổi trả.";
                return RedirectToAction("ReturnRequests");
            }

            if (returnRequest.Status != "Chấp nhận")
            {
                TempData["error"] = "Yêu cầu này chưa được chấp nhận hoặc đã hoàn tiền.";
                return RedirectToAction("ReturnDetails", new { id });
            }

            returnRequest.Status = "Đã hoàn tiền";
            if (returnRequest.Order != null)
            {
                returnRequest.Order.Status = "Đã hoàn trả";
            }
            await _context.SaveChangesAsync();

            // Gửi email xác nhận hoàn tiền
            await SendRefundCompletedEmailAsync(returnRequest);

            TempData["success"] = "Đã hoàn tất hoàn tiền cho khách hàng.";
            return RedirectToAction("ReturnDetails", new { id });
        }

        // POST: Admin/AdminOrder/BulkUpdateStatus - Cập nhật trạng thái hàng loạt
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkUpdateStatus(List<int> orderIds, string newStatus)
        {
            if (orderIds == null || !orderIds.Any())
            {
                TempData["error"] = "Vui lòng chọn ít nhất một đơn hàng.";
                return RedirectToAction("Index");
            }

            var validStatuses = new[] { "Chờ xác nhận", "Đã xác nhận", "Đang giao", "Đã giao", "Hoàn thành", "Đã hủy" };
            if (!validStatuses.Contains(newStatus))
            {
                TempData["error"] = "Trạng thái không hợp lệ.";
                return RedirectToAction("Index");
            }

            var orders = await _context.Orders
                .Where(o => orderIds.Contains(o.Id))
                .ToListAsync();

            foreach (var order in orders)
            {
                order.Status = newStatus;
            }

            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã cập nhật trạng thái {orders.Count} đơn hàng thành '{newStatus}'.";
            return RedirectToAction("Index");
        }

        // POST: Admin/AdminOrder/ReassignOrder - Chuyển đơn cho shipper khác
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReassignOrder(int id, string newShipperId)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Details", new { id });
            }

            if (string.IsNullOrEmpty(newShipperId))
            {
                TempData["error"] = "Vui lòng chọn shipper.";
                return RedirectToAction("Details", new { id });
            }

            // Kiểm tra shipper mới có tồn tại không
            var newShipper = await _userManager.FindByIdAsync(newShipperId);
            if (newShipper == null)
            {
                TempData["error"] = "Shipper không tồn tại.";
                return RedirectToAction("Details", new { id });
            }

            var oldShipperId = order.ShipperId;

            // Hủy Hangfire job reassignment cũ (nếu có)
            if (!string.IsNullOrEmpty(order.ReassignmentJobId))
            {
                BackgroundJob.Delete(order.ReassignmentJobId);
                order.ReassignmentJobId = null;
            }

            // Cập nhật shipper mới
            order.ShipperId = newShipperId;
            order.AssignedAt = DateTime.Now;
            order.ShipperStatus = "Đã phân công";
            order.ShipperConfirmedAt = null;

            await _context.SaveChangesAsync();

            // 📝 Log lịch sử reassign
            var assignmentHistory = new OrderAssignmentHistory
            {
                OrderId = id,
                ShipperId = newShipperId,
                AssignedAt = DateTime.Now,
                Response = null,
                RespondedAt = null,
                Notes = $"Admin manually reassigned from {oldShipperId ?? "unassigned"}"
            };
            _context.OrderAssignmentHistories.Add(assignmentHistory);
            await _context.SaveChangesAsync();

            // Cập nhật stats shipper cũ
            if (!string.IsNullOrEmpty(oldShipperId))
            {
                await _shipperAssignmentService.UpdateShipperStatsAsync(oldShipperId);
            }

            // Cập nhật stats shipper mới
            await _shipperAssignmentService.UpdateShipperStatsAsync(newShipperId);

            // Lên lịch timeout 3 phút
            var jobId = BackgroundJob.Schedule(
                () => _shipperAssignmentService.ReassignOrderAsync(id),
                TimeSpan.FromMinutes(3)
            );
            order.ReassignmentJobId = jobId;
            await _context.SaveChangesAsync();

            // 🔔 Gửi SignalR notification
            await _hubContext.Clients.All.SendAsync("ReceiveShipperUpdate", id, new
            {
                orderId = order.OrderId,
                shipperId = newShipper.Id,
                shipperName = newShipper.FullName ?? "N/A",
                shipperEmail = newShipper.Email ?? "N/A",
                shipperPhone = newShipper.PhoneNumber ?? "Chưa cập nhật",
                shipperStatus = "Đã phân công",
                assignedAt = order.AssignedAt?.ToString("o"),
                shipperConfirmedAt = (string?)null
            });

            TempData["success"] = $"Đã chuyển đơn hàng cho shipper: {newShipper.FullName}. Shipper có 3 phút để xác nhận.";
            return RedirectToAction("Details", new { id });
        }

        // GET: Admin/AdminOrder/GetAvailableShippers - API lấy danh sách shipper available
        [HttpGet]
        public async Task<IActionResult> GetAvailableShippers()
        {
            var shippers = await _shipperAssignmentService.GetAvailableShippersAsync();
            
            var shipperList = new List<object>();
            
            foreach (var shipper in shippers)
            {
                var user = await _userManager.FindByIdAsync(shipper.UserId);
                if (user != null)
                {
                    // Đếm tổng số đơn đã giao
                    var totalDelivered = await _context.Orders
                        .CountAsync(o => o.ShipperId == shipper.UserId && o.Status == "Hoàn thành");
                    
                    shipperList.Add(new
                    {
                        userId = shipper.UserId,
                        fullName = user.FullName ?? "N/A",
                        email = user.Email ?? "N/A",
                        phoneNumber = user.PhoneNumber,
                        currentActiveOrders = shipper.CurrentActiveOrders,
                        maxActiveOrders = shipper.MaxActiveOrders,
                        totalDeliveredOrders = totalDelivered
                    });
                }
            }
            
            return Json(shipperList);
        }

        // Hàm gửi email xác nhận đơn hàng
        private async Task SendOrderConfirmedEmailAsync(Order order)
        {
            var user = await _context.Users.FindAsync(order.UserId);
            var email = user?.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var subject = $"[Bloomie] Đơn hàng #{order.OrderId} đã được xác nhận";
                var body = $@"
                <!DOCTYPE html>
                <html lang='vi'>
                <head>
                    <meta charset='UTF-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
                        .container {{ max-width: 600px; margin: 30px auto; background-color: #fff; border-radius: 10px; box-shadow: 0 4px 12px rgba(0,0,0,0.08); overflow: hidden; }}
                        .header {{ background-color: #43a047; padding: 24px; text-align: center; }}
                        .header h1 {{ color: #fff; margin: 0; font-size: 28px; }}
                        .content {{ padding: 32px; color: #333; }}
                        .order-info {{ background-color: #f8f9fa; padding: 18px; border-radius: 6px; margin: 18px 0; }}
                        .footer {{ background-color: #f8f8f8; padding: 18px; text-align: center; font-size: 15px; color: #777; }}
                        .btn {{ display: inline-block; padding: 12px 24px; background-color: #43a047; color: #fff !important; text-decoration: none; font-size: 16px; font-weight: bold; border-radius: 5px; margin: 10px 5px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Bloomie Flower Shop</h1>
                        </div>
                        <div class='content'>
                            <h2>Đơn hàng của bạn đã được xác nhận</h2>
                            <div class='order-info'>
                                <strong>Mã đơn hàng:</strong> #{order.OrderId}<br/>
                                <strong>Thời gian xác nhận:</strong> {DateTime.Now:HH:mm dd/MM/yyyy}<br/>
                                <strong>Trạng thái:</strong> Đã xác nhận<br/>
                                <strong>Tổng tiền:</strong> {order.TotalAmount:N0} VNĐ<br/>
                            </div>
                            <p>Cảm ơn bạn đã đặt hàng tại Bloomie! Đơn hàng của bạn đã được xác nhận và sẽ sớm được xử lý.</p>
                            <p>Chúng tôi sẽ thông báo cho bạn khi đơn hàng được giao.</p>
                            <p>Nếu có thắc mắc hoặc cần hỗ trợ, hãy liên hệ với chúng tôi qua:</p>
                            <ul>
                                <li>📞 Hotline: <strong>0987 654 321</strong></li>
                                <li>📧 Email: <strong>bloomieshop25@gmail.com</strong></li>
                            </ul>
                            <div style='text-align:center; margin: 30px 0;'>
                                <a href='https://bloomie.vn/Order/Details/{order.Id}' class='btn'>Xem chi tiết đơn hàng</a>
                            </div>
                        </div>
                        <div class='footer'>
                            <p>© 2025 Bloomie Flower Shop. Email này được gửi tự động, vui lòng không trả lời.</p>
                        </div>
                    </div>
                </body>
                </html>
                ";
                await _emailService.SendEmailAsync(email, subject, body);
            }
        }

        // Hàm gửi email thông báo hủy đơn hàng
        private async Task SendOrderCancelledEmailAsync(Order order, string? cancelReason)
        {
            var user = await _context.Users.FindAsync(order.UserId);
            var email = user?.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var subject = $"[Bloomie] Đơn hàng #{order.OrderId} đã bị hủy";
                var reasonText = !string.IsNullOrEmpty(cancelReason)
                    ? $"<strong>Lý do:</strong> {cancelReason}<br/>"
                    : "";
                var body = $@"
                <!DOCTYPE html>
                <html lang='vi'>
                <head>
                    <meta charset='UTF-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
                        .container {{ max-width: 600px; margin: 30px auto; background-color: #fff; border-radius: 10px; box-shadow: 0 4px 12px rgba(0,0,0,0.08); overflow: hidden; }}
                        .header {{ background-color: #dc3545; padding: 24px; text-align: center; }}
                        .header h1 {{ color: #fff; margin: 0; font-size: 28px; }}
                        .content {{ padding: 32px; color: #333; }}
                        .order-info {{ background-color: #f8f9fa; padding: 18px; border-radius: 6px; margin: 18px 0; }}
                        .footer {{ background-color: #f8f8f8; padding: 18px; text-align: center; font-size: 15px; color: #777; }}
                        .btn {{ display: inline-block; padding: 12px 24px; background-color: #FF7043; color: #fff !important; text-decoration: none; font-size: 16px; font-weight: bold; border-radius: 5px; margin: 10px 5px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Bloomie Flower Shop</h1>
                        </div>
                        <div class='content'>
                            <h2>Đơn hàng của bạn đã bị hủy</h2>
                            <div class='order-info'>
                                <strong>Mã đơn hàng:</strong> #{order.OrderId}<br/>
                                <strong>Thời gian hủy:</strong> {DateTime.Now:HH:mm dd/MM/yyyy}<br/>
                                {reasonText}
                                <strong>Tổng tiền:</strong> {order.TotalAmount:N0} VNĐ<br/>
                            </div>
                            <p>Chúng tôi rất tiếc phải thông báo rằng đơn hàng của bạn đã bị hủy.</p>
                            <p>Nếu bạn vẫn muốn mua sản phẩm, vui lòng đặt hàng lại trên website.</p>
                            <p>Nếu có thắc mắc hoặc cần hỗ trợ, hãy liên hệ với chúng tôi qua:</p>
                            <ul>
                                <li>📞 Hotline: <strong>0987 654 321</strong></li>
                                <li>📧 Email: <strong>bloomieshop25@gmail.com</strong></li>
                            </ul>
                            <div style='text-align:center; margin: 30px 0;'>
                                <a href='https://bloomie.vn' class='btn'>Quay lại Bloomie Shop</a>
                            </div>
                        </div>
                        <div class='footer'>
                            <p>© 2025 Bloomie Flower Shop. Email này được gửi tự động, vui lòng không trả lời.</p>
                        </div>
                    </div>
                </body>
                </html>
                ";
                await _emailService.SendEmailAsync(email, subject, body);
            }
        }

        // Hàm gửi email khi đơn hàng đã giao thành công
        private async Task SendOrderDeliveredEmailAsync(Order order)
        {
            var user = await _context.Users.FindAsync(order.UserId);
            var email = user?.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var subject = $"[Bloomie] Đơn hàng #{order.OrderId} đã được giao thành công";
                var body = $@"
                <!DOCTYPE html>
                <html lang='vi'>
                <head>
                    <meta charset='UTF-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
                        .container {{ max-width: 600px; margin: 30px auto; background-color: #fff; border-radius: 10px; box-shadow: 0 4px 12px rgba(0,0,0,0.08); overflow: hidden; }}
                        .header {{ background-color: #43a047; padding: 24px; text-align: center; }}
                        .header h1 {{ color: #fff; margin: 0; font-size: 28px; }}
                        .content {{ padding: 32px; color: #333; }}
                        .order-info {{ background-color: #f8f9fa; padding: 18px; border-radius: 6px; margin: 18px 0; }}
                        .footer {{ background-color: #f8f8f8; padding: 18px; text-align: center; font-size: 15px; color: #777; }}
                        .btn {{ display: inline-block; padding: 12px 24px; background-color: #43a047; color: #fff !important; text-decoration: none; font-size: 16px; font-weight: bold; border-radius: 5px; margin: 10px 5px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Bloomie Flower Shop</h1>
                        </div>
                        <div class='content'>
                            <h2>Đơn hàng của bạn đã được giao thành công</h2>
                            <div class='order-info'>
                                <strong>Mã đơn hàng:</strong> #{order.OrderId}<br/>
                                <strong>Thời gian giao:</strong> {DateTime.Now:HH:mm dd/MM/yyyy}<br/>
                                <strong>Trạng thái:</strong> Đã giao<br/>
                                <strong>Tổng tiền:</strong> {order.TotalAmount:N0} VNĐ<br/>
                            </div>
                            <p>Cảm ơn bạn đã tin tưởng và mua hàng tại Bloomie! Nếu hài lòng với sản phẩm và dịch vụ, hãy để lại đánh giá cho chúng tôi nhé.</p>
                            <p>Nếu có thắc mắc hoặc cần hỗ trợ, hãy liên hệ với chúng tôi qua:</p>
                            <ul>
                                <li>📞 Hotline: <strong>0987 654 321</strong></li>
                                <li>📧 Email: <strong>bloomieshop25@gmail.com</strong></li>
                            </ul>
                            <div style='text-align:center; margin: 30px 0;'>
                                <a href='https://bloomie.vn/Order/Details/{order.Id}' class='btn'>Xem chi tiết đơn hàng</a>
                            </div>
                        </div>
                        <div class='footer'>
                            <p>© 2025 Bloomie Flower Shop. Email này được gửi tự động, vui lòng không trả lời.</p>
                        </div>
                    </div>
                </body>
                                </html>
                ";
                await _emailService.SendEmailAsync(email, subject, body);
            }
        }

        // Hàm gửi email chấp nhận đổi trả
        private async Task SendReturnApprovedEmailAsync(OrderReturn returnRequest)
        {
            var user = await _context.Users.FindAsync(returnRequest.Order!.UserId);
            var email = user?.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var subject = $"[Bloomie] Yêu cầu đổi trả đơn hàng #{returnRequest.Order.OrderId} đã được chấp nhận";
                var body = $@"<!DOCTYPE html><html><body><h2>Yêu cầu đổi trả đã được chấp nhận</h2><p>Mã đơn hàng: #{returnRequest.Order.OrderId}</p><p>Số tiền hoàn: {returnRequest.RefundAmount:N0} VNĐ</p></body></html>";
                await _emailService.SendEmailAsync(email, subject, body);
            }
        }

        // Hàm gửi email từ chối đổi trả
        private async Task SendReturnRejectedEmailAsync(OrderReturn returnRequest)
        {
            var user = await _context.Users.FindAsync(returnRequest.Order!.UserId);
            var email = user?.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var subject = $"[Bloomie] Yêu cầu đổi trả đơn hàng #{returnRequest.Order.OrderId} bị từ chối";
                var body = $@"<!DOCTYPE html><html><body><h2>Yêu cầu đổi trả bị từ chối</h2><p>Lý do: {returnRequest.AdminNote}</p></body></html>";
                await _emailService.SendEmailAsync(email, subject, body);
            }
        }

        // Hàm gửi email hoàn tất hoàn tiền
        private async Task SendRefundCompletedEmailAsync(OrderReturn returnRequest)
        {
            var user = await _context.Users.FindAsync(returnRequest.Order!.UserId);
            var email = user?.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var subject = $"[Bloomie] Đã hoàn tiền cho đơn hàng #{returnRequest.Order.OrderId}";
                var body = $@"<!DOCTYPE html><html><body><h2>Đã hoàn tiền thành công</h2><p>Số tiền: {returnRequest.RefundAmount:N0} VNĐ</p></body></html>";
                await _emailService.SendEmailAsync(email, subject, body);
            }
        }
    }
}
