using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Bloomie.Hubs;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bloomie.Services.Implementations
{
    public class ShipperAssignmentService : IShipperAssignmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<ShipperAssignmentService>? _logger;

        public ShipperAssignmentService(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager,
            IHubContext<NotificationHub> hubContext,
            ILogger<ShipperAssignmentService>? logger = null)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Tự động phân công đơn hàng cho shipper theo thuật toán Round Robin
        /// </summary>
        public async Task<bool> AssignOrderToShipperAsync(int orderId)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null || order.ShipperId != null)
                return false; // Đơn không tồn tại hoặc đã được phân công

            // Lấy danh sách shipper có thể nhận đơn
            var availableShippers = await GetAvailableShippersAsync();
            if (!availableShippers.Any())
                return false; // Không có shipper nào khả dụng

            // Round Robin: Chọn shipper có LastAssignedAt lâu nhất (hoặc chưa từng nhận)
            var selectedShipper = availableShippers
                .OrderBy(s => s.LastAssignedAt ?? DateTime.MinValue)
                .First();

            // Cập nhật thông tin phân công trong Order
            order.ShipperId = selectedShipper.UserId;
            order.AssignedAt = DateTime.Now;
            order.ShipperStatus = "Đã phân công";

            // Cập nhật thống kê shipper (chỉ update LastAssignedAt, CHƯA tăng CurrentActiveOrders)
            // CurrentActiveOrders sẽ tăng khi shipper Confirm đơn
            selectedShipper.LastAssignedAt = DateTime.Now;
            selectedShipper.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            // 📝 Log lịch sử phân công vào database
            var assignmentHistory = new OrderAssignmentHistory
            {
                OrderId = orderId,
                ShipperId = selectedShipper.UserId,
                AssignedAt = DateTime.Now,
                Response = null, // Chờ shipper response
                RespondedAt = null,
                Notes = "Auto-assigned via Round Robin algorithm"
            };
            _context.OrderAssignmentHistories.Add(assignmentHistory);
            await _context.SaveChangesAsync();

            // Lên lịch Hangfire job để tự động re-assign nếu shipper không confirm trong 3 phút
            var jobId = BackgroundJob.Schedule(
                () => ReassignOrderAsync(orderId),
                TimeSpan.FromMinutes(3)
            );

            // Lưu JobId để có thể hủy sau
            order.ReassignmentJobId = jobId;
            await _context.SaveChangesAsync();

            // 🔔 Gửi SignalR notification về shipper mới được assign
            var shipper = await _userManager.FindByIdAsync(selectedShipper.UserId);
            await _hubContext.Clients.All.SendAsync("ReceiveShipperUpdate", orderId, new
            {
                orderId = order.OrderId,
                shipperId = shipper?.Id,
                shipperName = shipper?.FullName ?? "N/A",
                shipperEmail = shipper?.Email ?? "N/A",
                shipperPhone = shipper?.PhoneNumber ?? "Chưa cập nhật",
                shipperStatus = "Đã phân công",
                assignedAt = order.AssignedAt?.ToString("o"),
                shipperConfirmedAt = (string?)null
            });

            return true;
        }

        /// <summary>
        /// Lấy danh sách shipper có thể nhận đơn (đang làm việc và chưa quá tải)
        /// </summary>
        public async Task<List<ShipperProfile>> GetAvailableShippersAsync()
        {
            return await _context.ShipperProfiles
                .Where(s => s.IsWorking && s.CurrentActiveOrders < s.MaxActiveOrders)
                .ToListAsync();
        }

        /// <summary>
        /// Cập nhật số đơn hiện tại của shipper (tính lại từ Orders)
        /// </summary>
        public async Task UpdateShipperStatsAsync(string userId)
        {
            var shipperProfile = await _context.ShipperProfiles
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (shipperProfile == null)
                return;

            // Đếm số đơn đang active (chỉ đếm đơn đã Confirmed, không đếm Assigned vì chưa chắc shipper nhận)
                var activeOrders = await _context.Orders
                    .CountAsync(o => o.ShipperId == userId 
                    && o.ShipperStatus == "Đã xác nhận"
                    && o.Status != "Hoàn thành" 
                    && o.Status != "Đã hủy");

            shipperProfile.CurrentActiveOrders = activeOrders;
            shipperProfile.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Hủy phân công và gán lại cho shipper khác (khi timeout)
        /// </summary>
        public async Task<bool> ReassignOrderAsync(int orderId)
        {
            var order = await _context.Orders.FindAsync(orderId);
            
            if (order == null)
                return false;

            // Chỉ re-assign nếu vẫn ở trạng thái "Đã phân công" (chưa confirm)
            if (order.ShipperStatus != "Đã phân công")
                return false; // Shipper đã confirm rồi, không cần re-assign

            var oldShipperId = order.ShipperId;

            // Hủy phân công cũ
            order.ShipperId = null;
            order.AssignedAt = null;
            order.ShipperStatus = null;
            order.ReassignmentJobId = null;

            await _context.SaveChangesAsync();

            // Cập nhật lại stats của shipper cũ
            if (!string.IsNullOrEmpty(oldShipperId))
            {
                await UpdateShipperStatsAsync(oldShipperId);
                
                // 📝 Log lịch sử timeout/reject vào database
                var lastAssignment = await _context.OrderAssignmentHistories
                    .Where(h => h.OrderId == orderId && h.ShipperId == oldShipperId && h.Response == null)
                    .OrderByDescending(h => h.AssignedAt)
                    .FirstOrDefaultAsync();
                    
                if (lastAssignment != null)
                {
                    lastAssignment.Response = "Timeout";
                    lastAssignment.RespondedAt = DateTime.Now;
                    lastAssignment.Notes = "Shipper did not confirm within 3 minutes";
                    await _context.SaveChangesAsync();
                }
            }

            // 🔔 Gửi SignalR notification về việc unassign shipper (timeout)
            await _hubContext.Clients.All.SendAsync("ReceiveShipperUpdate", orderId, new
            {
                orderId = order.OrderId,
                shipperId = (string?)null,
                shipperName = (string?)null,
                shipperEmail = (string?)null,
                shipperPhone = (string?)null,
                shipperStatus = "Đã quá hạn",
                assignedAt = (string?)null,
                shipperConfirmedAt = (string?)null
            });

            // Thử phân công lại cho shipper khác
            return await AssignOrderToShipperAsync(orderId);
        }

        /// <summary>
        /// Xác nhận shipper đã nhận hoa (hủy Hangfire job)
        /// </summary>
        public async Task<bool> ConfirmPickupAsync(int orderId, string shipperId)
        {
            var order = await _context.Orders.FindAsync(orderId);
            
            if (order == null || order.ShipperId != shipperId)
                return false; // Đơn không tồn tại hoặc không phải của shipper này

            if (order.ShipperStatus != "Đã phân công")
                return false; // Đã confirm hoặc đã hủy

            // Cập nhật trạng thái confirm
            order.ShipperStatus = "Đã xác nhận";
            order.ShipperConfirmedAt = DateTime.Now;

            // ✅ Tăng CurrentActiveOrders khi shipper confirm (không phải lúc assign)
            var shipperProfile = await _context.ShipperProfiles
                .FirstOrDefaultAsync(sp => sp.UserId == shipperId);
            if (shipperProfile != null)
            {
                shipperProfile.CurrentActiveOrders++;
                shipperProfile.UpdatedAt = DateTime.Now;
            }

            // 📝 Log lịch sử acceptance vào database
            var lastAssignment = await _context.OrderAssignmentHistories
                .Where(h => h.OrderId == orderId && h.ShipperId == shipperId && h.Response == null)
                .OrderByDescending(h => h.AssignedAt)
                .FirstOrDefaultAsync();
                
            if (lastAssignment != null)
            {
                lastAssignment.Response = "Accepted";
                lastAssignment.RespondedAt = DateTime.Now;
                lastAssignment.Notes = "Shipper confirmed pickup";
            }

            // Hủy Hangfire job re-assignment
            if (!string.IsNullOrEmpty(order.ReassignmentJobId))
            {
                BackgroundJob.Delete(order.ReassignmentJobId);
                order.ReassignmentJobId = null;
            }

            await _context.SaveChangesAsync();

            // 🔔 Gửi SignalR notification về shipper confirm
            var shipper = await _userManager.FindByIdAsync(shipperId);
            await _hubContext.Clients.All.SendAsync("ReceiveShipperUpdate", orderId, new
            {
                orderId = order.OrderId,
                shipperId = shipper?.Id,
                shipperName = shipper?.FullName ?? "N/A",
                shipperEmail = shipper?.Email ?? "N/A",
                shipperPhone = shipper?.PhoneNumber ?? "Chưa cập nhật",
                shipperStatus = "Confirmed",
                assignedAt = order.AssignedAt?.ToString("o"),
                shipperConfirmedAt = order.ShipperConfirmedAt?.ToString("o")
            });

            return true;
        }

        /// <summary>
        /// ⭐ Tự động phân công shipper cho các đơn đặt trước có ngày giao = HÔM NAY
        /// Được gọi bởi Hangfire RecurringJob mỗi 30 phút
        /// </summary>
        [AutomaticRetry(Attempts = 3)]
        public async Task AutoAssignPreOrdersForToday()
        {
            var today = DateTime.Today;

            _logger?.LogInformation($"🔍 Checking pre-orders for today ({today:dd/MM/yyyy})...");

            // Tìm tất cả đơn hàng:
            // 1. Trạng thái "Đã xác nhận"
            // 2. Chưa có shipper
            // 3. Có sản phẩm giao HÔM NAY
            var ordersToAssign = await _context.Orders
                .Include(o => o.OrderDetails)
                .Where(o => o.Status == "Đã xác nhận" 
                    && string.IsNullOrEmpty(o.ShipperId)
                    && o.OrderDetails!.Any(d => d.DeliveryDate != null && d.DeliveryDate.Value.Date == today))
                .ToListAsync();

            if (!ordersToAssign.Any())
            {
                _logger?.LogInformation("✅ No pre-orders to assign for today.");
                return;
            }

            _logger?.LogInformation($"📦 Found {ordersToAssign.Count} pre-order(s) for today. Assigning shippers...");

            int successCount = 0;
            int failCount = 0;

            foreach (var order in ordersToAssign)
            {
                try
                {
                    // Lấy thông tin ngày giao và khung giờ
                    var deliveryDetail = order.OrderDetails?
                        .Where(d => d.DeliveryDate != null && d.DeliveryDate.Value.Date == today)
                        .OrderBy(d => d.DeliveryDate)
                        .FirstOrDefault();

                    var deliveryTime = deliveryDetail?.DeliveryTime ?? "chưa xác định";

                    // Phân công shipper tự động
                    var success = await AssignOrderToShipperAsync(order.Id);

                    if (success)
                    {
                        successCount++;
                        
                        // Reload order để lấy ShipperId mới
                        await _context.Entry(order).ReloadAsync();
                        
                        var shipper = await _userManager.FindByIdAsync(order.ShipperId!);
                        var shipperName = shipper?.FullName ?? "N/A";

                        _logger?.LogInformation($"✅ Auto-assigned shipper '{shipperName}' to pre-order #{order.OrderId} (Delivery: {deliveryTime})");

                        // Gửi notification cho Admin qua SignalR
                        try
                        {
                            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
                            {
                                title = "🚀 Tự động phân công shipper",
                                message = $"Đã phân công {shipperName} cho đơn đặt trước #{order.OrderId} (Giao {deliveryTime})",
                                link = $"/Admin/AdminOrder/Details/{order.Id}",
                                type = "success",
                                timestamp = DateTime.Now
                            });
                        }
                        catch { }
                    }
                    else
                    {
                        failCount++;
                        _logger?.LogWarning($"⚠️ Failed to assign shipper for pre-order #{order.OrderId} - No shipper available");

                        // Gửi cảnh báo cho Admin
                        try
                        {
                            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
                            {
                                title = "⚠️ Không thể phân công shipper",
                                message = $"Đơn đặt trước #{order.OrderId} (Giao {deliveryTime}) - Không có shipper khả dụng!",
                                link = $"/Admin/AdminOrder/Details/{order.Id}",
                                type = "warning",
                                timestamp = DateTime.Now
                            });
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger?.LogError(ex, $"❌ Error auto-assigning shipper for order #{order.OrderId}");
                }
            }

            _logger?.LogInformation($"📊 Auto-assignment completed: {successCount} success, {failCount} failed");
        }

        /// <summary>
        /// ⏰ Kiểm tra đơn hàng URGENT (còn < 1 giờ đến giờ giao mà chưa có shipper confirm)
        /// Được gọi bởi Hangfire RecurringJob mỗi 10 phút
        /// </summary>
        [AutomaticRetry(Attempts = 3)]
        public async Task CheckUrgentOrders()
        {
            var now = DateTime.Now;
            var oneHourLater = now.AddHours(1);

            _logger?.LogInformation($"🔍 Checking URGENT orders (delivery time < 1 hour)...");

            // Tìm đơn hàng:
            // 1. Trạng thái "Đã xác nhận"
            // 2. ShipperStatus != "Confirmed" (chưa có shipper confirm hoặc chưa assign)
            // 3. DeliveryDate + DeliveryTime < 1 giờ nữa
            var urgentOrders = await _context.Orders
                .Include(o => o.OrderDetails)
                .Where(o => o.Status == "Đã xác nhận" 
                    && o.ShipperStatus != "Confirmed"
                    && o.OrderDetails!.Any(d => d.DeliveryDate != null && d.DeliveryDate.Value.Date == now.Date))
                .ToListAsync();

            var criticalOrders = new List<(Order order, DateTime deliveryDateTime, string deliveryTime)>();

            foreach (var order in urgentOrders)
            {
                var deliveryDetail = order.OrderDetails?
                    .Where(d => d.DeliveryDate != null && d.DeliveryDate.Value.Date == now.Date)
                    .OrderBy(d => d.DeliveryDate)
                    .FirstOrDefault();

                if (deliveryDetail?.DeliveryDate == null)
                    continue;

                var deliveryTime = deliveryDetail.DeliveryTime;
                if (string.IsNullOrEmpty(deliveryTime))
                    continue;

                // Parse delivery time (format: "08:00 - 10:00" hoặc "14:00 - 16:00")
                var timeParts = deliveryTime.Split('-');
                if (timeParts.Length < 1)
                    continue;

                var startTime = timeParts[0].Trim();
                if (!TimeSpan.TryParse(startTime, out var deliveryTimeSpan))
                    continue;

                var deliveryDateTime = deliveryDetail.DeliveryDate.Value.Date.Add(deliveryTimeSpan);

                // Nếu còn < 1 giờ đến giờ giao → URGENT
                if (deliveryDateTime <= oneHourLater && deliveryDateTime > now)
                {
                    criticalOrders.Add((order, deliveryDateTime, deliveryTime));
                }
            }

            if (!criticalOrders.Any())
            {
                _logger?.LogInformation("✅ No URGENT orders found.");
                return;
            }

            _logger?.LogWarning($"🚨 Found {criticalOrders.Count} URGENT order(s)!");

            foreach (var (order, deliveryDateTime, deliveryTime) in criticalOrders)
            {
                var minutesLeft = (int)(deliveryDateTime - now).TotalMinutes;
                var shipperStatus = order.ShipperStatus ?? "Chưa phân công";

                _logger?.LogWarning($"🚨 URGENT: Order #{order.OrderId} - Delivery in {minutesLeft} minutes ({deliveryTime}), Shipper Status: {shipperStatus}");

                // Gửi thông báo KHẨN CẤP cho Admin
                try
                {
                    var message = shipperStatus == "Assigned"
                        ? $"Đơn #{order.OrderId} sắp đến giờ giao ({deliveryTime}) trong {minutesLeft} phút nhưng shipper CHƯA XÁC NHẬN!"
                        : $"Đơn #{order.OrderId} sắp đến giờ giao ({deliveryTime}) trong {minutesLeft} phút nhưng CHƯA CÓ SHIPPER!";

                    await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
                    {
                        title = "🚨 ĐƠN HÀNG KHẨN CẤP",
                        message = message,
                        link = $"/Admin/AdminOrder/Details/{order.Id}",
                        type = "error",
                        timestamp = DateTime.Now,
                        urgent = true
                    });

                    _logger?.LogInformation($"✅ Sent URGENT notification for order #{order.OrderId}");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"❌ Failed to send URGENT notification for order #{order.OrderId}");
                }
            }
        }
    }
}
