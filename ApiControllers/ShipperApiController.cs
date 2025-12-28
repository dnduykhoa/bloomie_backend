using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Bloomie.Data;
using Bloomie.Models.ApiRequests;
using Bloomie.Hubs;
using Bloomie.Services.Interfaces;
using System.Security.Claims;

namespace Bloomie.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Shipper,Admin,Manager")]
    public class ShipperApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IShipperAssignmentService _shipperAssignmentService;

        public ShipperApiController(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hubContext,
            IShipperAssignmentService shipperAssignmentService)
        {
            _context = context;
            _hubContext = hubContext;
            _shipperAssignmentService = shipperAssignmentService;
        }

        /// <summary>
        /// Lấy thông tin shipper hiện tại (authenticated user)
        /// </summary>
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrentShipper()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                    return NotFound(new { message = "Không tìm thấy thông tin shipper." });

                return Ok(new { 
                    success = true, 
                    data = new
                    {
                        UserName = user.UserName,
                        FullName = user.FullName,
                        Email = user.Email,
                        PhoneNumber = user.PhoneNumber,
                        UserId = userId
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server.", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy thống kê shipper (tổng đơn, hoàn thành, thu nhập, rating)
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetShipperStats()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                // Lấy ShipperProfile nếu có
                var shipperProfile = await _context.ShipperProfiles
                    .FirstOrDefaultAsync(s => s.UserId == userId);

                // Tính toán stats từ Orders
                var totalDeliveries = await _context.Orders
                    .CountAsync(o => o.ShipperId == userId);

                // Đếm đơn đã hoàn thành (bao gồm "Đã giao" và "Hoàn thành")
                var completedDeliveries = await _context.Orders
                    .CountAsync(o => o.ShipperId == userId && 
                        (o.Status == "Đã giao" || o.Status == "Hoàn thành"));

                // Tính tổng thu nhập từ ShippingFee thực tế (không hard-code 30k)
                var totalEarnings = await _context.Orders
                    .Where(o => o.ShipperId == userId && 
                        (o.Status == "Đã giao" || o.Status == "Hoàn thành"))
                    .SumAsync(o => o.ShippingFee);


                return Ok(new { 
                    success = true, 
                    data = new
                    {
                        TotalDeliveries = totalDeliveries,
                        CompletedDeliveries = completedDeliveries,
                        TotalEarnings = totalEarnings,
                        CurrentActiveOrders = shipperProfile?.CurrentActiveOrders ?? 0
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server.", error = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật vị trí hiện tại của shipper khi đang giao hàng
        /// </summary>
        [HttpPost("update-location")]
        public async Task<IActionResult> UpdateLocation([FromBody] LocationUpdateRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                // Tìm đơn hàng được phân công cho shipper này
                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == request.OrderId && 
                                            o.ShipperId == userId &&
                                            (o.Status == "Đã xác nhận" || o.Status == "Đang giao"));

                if (order == null)
                    return NotFound(new { message = "Không tìm thấy đơn hàng hoặc bạn không được phân công giao đơn này." });

                // Cập nhật vị trí
                order.LastKnownLatitude = request.Latitude;
                order.LastKnownLongitude = request.Longitude;
                order.LastGPSUpdate = DateTime.Now; // ✅ Thêm timestamp
                order.Status = "Đang giao"; // Cập nhật status khi shipper bắt đầu di chuyển

                await _context.SaveChangesAsync();

                // ✅ Gửi real-time update cho Admin/Manager thông qua SignalR với event name đúng
                await _hubContext.Clients.All.SendAsync("ReceiveShipperLocation", order.Id, new
                {
                    orderId = order.OrderId,
                    latitude = request.Latitude,
                    longitude = request.Longitude,
                    timestamp = DateTime.Now.ToString("o")
                });

                return Ok(new { 
                    success = true, 
                    message = "Đã cập nhật vị trí thành công.",
                    data = new
                    {
                        OrderId = order.Id,
                        Latitude = request.Latitude,
                        Longitude = request.Longitude,
                        Status = order.Status
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi cập nhật vị trí.", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách đơn hàng đang giao của shipper
        /// </summary>
        [HttpGet("my-deliveries")]
        public async Task<IActionResult> GetMyDeliveries()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var deliveries = await _context.Orders
                    .Where(o => o.ShipperId == userId && 
                               // Chỉ lấy đơn chưa hoàn thành
                               o.Status != "Đã giao" && 
                               o.Status != "Hoàn thành" && 
                               o.Status != "Đã hủy" &&
                               o.Status != "Giao thất bại")
                    .Select(o => new ShipperLocationResponse
                    {
                        OrderId = o.Id,
                        OrderNumber = o.OrderId,
                        CustomerName = o.ReceiverName,
                        DeliveryAddress = o.ShippingAddress,
                        CurrentLatitude = o.LastKnownLatitude,
                        CurrentLongitude = o.LastKnownLongitude,
                        Status = o.Status,
                        ShipperStatus = o.ShipperStatus,  // Trả về ShipperStatus để app biết
                        AssignedAt = o.AssignedAt
                    })
                    .OrderByDescending(o => o.AssignedAt)
                    .ToListAsync();

                return Ok(new { success = true, data = deliveries, count = deliveries.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi lấy danh sách giao hàng.", error = ex.Message });
            }
        }

        /// <summary>
        /// Xem chi tiết đơn hàng của shipper
        /// </summary>
        [HttpGet("order-details/{orderId}")]
        public async Task<IActionResult> GetOrderDetails(int orderId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var order = await _context.Orders
                    .Include(o => o.OrderDetails!)
                        .ThenInclude(od => od.Product)
                    .FirstOrDefaultAsync(o => o.Id == orderId && o.ShipperId == userId);

                if (order == null)
                    return NotFound(new { message = "Không tìm thấy đơn hàng hoặc bạn không được phân công giao đơn này." });

                // Lấy thông tin user từ UserId
                var user = !string.IsNullOrEmpty(order.UserId) ? 
                    await _context.Users.FindAsync(order.UserId) : null;

                // ⭐ Fallback logic cho ShipperStatus khi null (đơn cũ hoặc Admin cập nhật thủ công)
                // Nếu ShipperStatus = null nhưng có ShipperId → coi như đã xác nhận
                var effectiveShipperStatus = order.ShipperStatus;
                if (string.IsNullOrEmpty(effectiveShipperStatus) && !string.IsNullOrEmpty(order.ShipperId))
                {
                    effectiveShipperStatus = "Đã xác nhận"; // Giả định đã confirm
                }

                var orderDetail = new
                {
                    OrderId = order.Id,
                    OrderNumber = order.OrderId,
                    CustomerInfo = new
                    {
                        Name = user?.FullName ?? order.ReceiverName ?? "Không rõ",
                        Phone = order.Phone,
                        Email = user?.Email
                    },
                    DeliveryInfo = new
                    {
                        Address = order.ShippingAddress,
                        Date = order.DeliveryDate,
                        Notes = order.Note,
                        ImageUrl = order.DeliveryImageUrl,  // ✅ Thêm URL ảnh minh chứng
                        ImageUploadedAt = order.DeliveryImageUploadedAt  // ✅ Thêm thời gian upload
                    },
                    Items = order.OrderDetails?.Select(od => new
                    {
                        ProductName = od.Product?.Name ?? "Sản phẩm",
                        ProductImage = od.Product?.ImageUrl ?? "/images/no-image.png", // ✅ Thêm ảnh sản phẩm
                        Quantity = od.Quantity,
                        UnitPrice = od.UnitPrice,
                        Total = od.Quantity * od.UnitPrice,
                        IsGift = od.IsGift
                    }).ToList(),
                    TotalAmount = order.TotalAmount,
                    PaymentMethod = order.PaymentMethod,
                    Status = order.Status,
                    ShipperStatus = effectiveShipperStatus,  // ✅ Dùng effective status (có fallback)
                    AssignedAt = order.AssignedAt,
                    ShipperConfirmedAt = order.ShipperConfirmedAt,  // ✅ Thêm ShipperConfirmedAt
                    CurrentLocation = new
                    {
                        Latitude = order.LastKnownLatitude,
                        Longitude = order.LastKnownLongitude
                    }
                };

                return Ok(new { success = true, data = orderDetail });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi lấy chi tiết đơn hàng.", error = ex.Message });
            }
        }

        /// <summary>
        /// Upload ảnh bằng chứng giao hàng
        /// </summary>
        [HttpPost("upload-delivery-proof/{orderId}")]
        public async Task<IActionResult> UploadDeliveryProof(int orderId, IFormFile image)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                if (image == null || image.Length == 0)
                    return BadRequest(new { message = "Vui lòng chọn ảnh bằng chứng giao hàng." });

                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == orderId && o.ShipperId == userId);

                if (order == null)
                    return NotFound(new { message = "Không tìm thấy đơn hàng." });

                // Kiểm tra kích thước và định dạng ảnh
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var fileExtension = Path.GetExtension(image.FileName).ToLowerInvariant();
                
                if (!allowedExtensions.Contains(fileExtension))
                    return BadRequest(new { message = "Chỉ hỗ trợ ảnh định dạng JPG, JPEG, PNG." });

                if (image.Length > 5 * 1024 * 1024) // 5MB
                    return BadRequest(new { message = "Kích thước ảnh không được vượt quá 5MB." });

                // Tạo tên file duy nhất
                var fileName = $"delivery_proof_{orderId}_{DateTime.UtcNow:yyyyMMddHHmmss}{fileExtension}";
                var uploadPath = Path.Combine("wwwroot", "uploads", "delivery-proofs");
                
                // Tạo thư mục nếu chưa tồn tại
                if (!Directory.Exists(uploadPath))
                    Directory.CreateDirectory(uploadPath);

                var filePath = Path.Combine(uploadPath, fileName);

                // Lưu file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }

                // Cập nhật đường dẫn ảnh vào database
                var imageUrl = $"/uploads/delivery-proofs/{fileName}";
                
                // Cập nhật ảnh bằng chứng giao hàng
                order.DeliveryImageUrl = imageUrl;
                order.DeliveryImageUploadedAt = DateTime.Now; // ✅ Lưu thời gian chụp ảnh
                
                await _context.SaveChangesAsync();

                // Thông báo cho Admin
                await _hubContext.Clients.All.SendAsync("DeliveryProofUploaded", new
                {
                    OrderId = order.Id,
                    OrderNumber = order.OrderId,
                    ShipperId = userId,
                    ImageUrl = imageUrl,
                    UploadedAt = DateTime.UtcNow
                });

                return Ok(new { 
                    success = true, 
                    message = "Đã tải lên ảnh bằng chứng thành công.",
                    data = new
                    {
                        ImageUrl = imageUrl,
                        FileName = fileName
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi tải ảnh.", error = ex.Message });
            }
        }

        /// <summary>
        /// Admin/Manager xem vị trí tất cả shipper đang giao hàng
        /// </summary>
        [HttpGet("all-active-deliveries")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetAllActiveDeliveries()
        {
            try
            {
                var activeDeliveries = await _context.Orders
                    .Where(o => o.ShipperId != null && 
                               (o.Status == "Đã giao cho shipper" || o.Status == "Đang giao hàng") &&
                               o.LastKnownLatitude != null && o.LastKnownLongitude != null)
                    .Join(_context.Users, o => o.ShipperId, u => u.Id, (o, u) => new { Order = o, Shipper = u })
                    .Select(x => new
                    {
                        OrderId = x.Order.Id,
                        OrderNumber = x.Order.OrderId,
                        CustomerName = x.Order.ReceiverName,
                        DeliveryAddress = x.Order.ShippingAddress,
                        ShipperId = x.Order.ShipperId,
                        ShipperName = x.Shipper.FullName,
                        CurrentLatitude = x.Order.LastKnownLatitude,
                        CurrentLongitude = x.Order.LastKnownLongitude,
                        Status = x.Order.Status,
                        AssignedAt = x.Order.AssignedAt,
                        DeliveryDate = x.Order.DeliveryDate,
                        Phone = x.Order.Phone
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = activeDeliveries });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi lấy thông tin giao hàng.", error = ex.Message });
            }
        }

        /// <summary>
        /// Shipper xác nhận hoàn thành giao hàng
        /// </summary>
        [HttpPost("complete-delivery/{orderId}")]
        public async Task<IActionResult> CompleteDelivery(int orderId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == orderId && o.ShipperId == userId);

                if (order == null)
                    return NotFound(new { message = "Không tìm thấy đơn hàng." });

                // Kiểm tra xem có ảnh chứng minh giao hàng chưa
                if (string.IsNullOrEmpty(order.DeliveryImageUrl))
                    return BadRequest(new { message = "Vui lòng upload ảnh chứng minh giao hàng trước khi hoàn thành." });

                order.Status = "Đã giao";
                order.DeliveryDate = DateTime.UtcNow;
                
                await _context.SaveChangesAsync();

                // ⭐ CẬP NHẬT STATS SHIPPER - Giảm CurrentActiveOrders
                await _shipperAssignmentService.UpdateShipperStatsAsync(userId);

                // Thông báo real-time cho Admin
                await _hubContext.Clients.All.SendAsync("DeliveryCompleted", new
                {
                    OrderId = order.Id,
                    OrderNumber = order.OrderId,
                    ShipperId = userId,
                    CompletedAt = order.DeliveryDate
                });

                return Ok(new { success = true, message = "Đã xác nhận hoàn thành giao hàng. Bạn có thể nhận thêm đơn mới." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi xác nhận giao hàng.", error = ex.Message });
            }
        }

        /// <summary>
        /// Shipper xác nhận NHẬN đơn hàng (khi được phân công)
        /// </summary>
        [HttpPost("confirm-order/{orderId}")]
        public async Task<IActionResult> ConfirmOrder(int orderId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == orderId && o.ShipperId == userId);

                if (order == null)
                    return NotFound(new { message = "Không tìm thấy đơn hàng hoặc bạn không được phân công." });

                if (order.ShipperStatus != "Đã phân công")
                    return BadRequest(new { message = "Đơn hàng này đã được xác nhận hoặc từ chối trước đó." });

                // Cập nhật trạng thái
                order.ShipperStatus = "Đã xác nhận";
                order.ShipperConfirmedAt = DateTime.Now;
                
                await _context.SaveChangesAsync();

                // Hủy job timeout (nếu có)
                if (!string.IsNullOrEmpty(order.ReassignmentJobId))
                {
                    Hangfire.BackgroundJob.Delete(order.ReassignmentJobId);
                    order.ReassignmentJobId = null;
                    await _context.SaveChangesAsync();
                }

                // Log lịch sử xác nhận
                var assignmentHistory = await _context.OrderAssignmentHistories
                    .Where(h => h.OrderId == orderId && h.ShipperId == userId && h.Response == null)
                    .OrderByDescending(h => h.AssignedAt)
                    .FirstOrDefaultAsync();

                if (assignmentHistory != null)
                {
                    assignmentHistory.Response = "Accepted";
                    assignmentHistory.RespondedAt = DateTime.Now;
                    await _context.SaveChangesAsync();
                }

                // Gửi SignalR notification
                await _hubContext.Clients.All.SendAsync("ReceiveShipperUpdate", orderId, new
                {
                    orderId = order.OrderId,
                    shipperId = userId,
                    shipperStatus = "Đã xác nhận",
                    shipperConfirmedAt = order.ShipperConfirmedAt?.ToString("o")
                });  

                return Ok(new { 
                    success = true, 
                    message = "Đã xác nhận nhận đơn hàng. Bạn có thể bắt đầu giao hàng.",
                    data = new
                    {
                        OrderId = order.Id,
                        OrderNumber = order.OrderId,
                        ShipperStatus = order.ShipperStatus,
                        ConfirmedAt = order.ShipperConfirmedAt
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi xác nhận đơn hàng.", error = ex.Message });
            }
        }

        /// <summary>
        /// Shipper báo GIAO HÀNG THẤT BẠI (không liên lạc được khách, địa chỉ sai, khách từ chối nhận...)
        /// </summary>
        [HttpPost("fail-delivery/{orderId}")]
        public async Task<IActionResult> FailDelivery(int orderId, [FromBody] FailDeliveryRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == orderId && o.ShipperId == userId);

                if (order == null)
                    return NotFound(new { message = "Không tìm thấy đơn hàng hoặc bạn không được phân công giao đơn này." });

                if (order.Status != "Đang giao")
                    return BadRequest(new { message = "Chỉ có thể báo giao thất bại khi đơn hàng đang trong quá trình giao." });

                // Cập nhật trạng thái đơn hàng
                order.Status = "Giao thất bại";
                order.CancelReason = $"[Shipper] {request.Reason ?? "Không thể giao hàng"}";
                if (!string.IsNullOrEmpty(request.Note))
                {
                    order.Note = (order.Note ?? "") + $"\n[Ghi chú shipper]: {request.Note}";
                }
                order.CancelledAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Cập nhật stats shipper - giảm số đơn đang giao
                await _shipperAssignmentService.UpdateShipperStatsAsync(userId);

                // Gửi SignalR notification cho Admin
                await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
                {
                    orderStatus = order.Status,
                    paymentStatus = order.PaymentStatus,
                    failureReason = order.CancelReason
                });

                // Gửi notification riêng cho Admin
                await _hubContext.Clients.All.SendAsync("DeliveryFailed", new
                {
                    OrderId = order.Id,
                    OrderNumber = order.OrderId,
                    ShipperId = userId,
                    Reason = request.Reason,
                    Note = request.Note,
                    FailedAt = DateTime.Now
                });

                return Ok(new { 
                    success = true, 
                    message = "Đã ghi nhận giao hàng thất bại. Admin sẽ xử lý đơn hàng này.",
                    data = new
                    {
                        OrderId = order.Id,
                        OrderNumber = order.OrderId,
                        Status = order.Status,
                        Reason = request.Reason
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi báo giao hàng thất bại.", error = ex.Message });
            }
        }

        /// <summary>
        /// Shipper TỪ CHỐI đơn hàng (khi được phân công)
        /// </summary>
        [HttpPost("reject-order/{orderId}")]
        public async Task<IActionResult> RejectOrder(int orderId, [FromBody] RejectOrderRequest? request)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == orderId && o.ShipperId == userId);

                if (order == null)
                    return NotFound(new { message = "Không tìm thấy đơn hàng hoặc bạn không được phân công." });

                if (order.ShipperStatus != "Đã phân công")
                    return BadRequest(new { message = "Không thể từ chối đơn hàng này." });

                // Hủy phân công
                order.ShipperId = null;
                order.AssignedAt = null;
                order.ShipperStatus = null;
                order.ShipperConfirmedAt = null;

                // Hủy job timeout (nếu có)
                if (!string.IsNullOrEmpty(order.ReassignmentJobId))
                {
                    Hangfire.BackgroundJob.Delete(order.ReassignmentJobId);
                    order.ReassignmentJobId = null;
                }

                await _context.SaveChangesAsync();

                // Log lịch sử từ chối
                var assignmentHistory = await _context.OrderAssignmentHistories
                    .Where(h => h.OrderId == orderId && h.ShipperId == userId && h.Response == null)
                    .OrderByDescending(h => h.AssignedAt)
                    .FirstOrDefaultAsync();

                if (assignmentHistory != null)
                {
                    assignmentHistory.Response = "Rejected";
                    assignmentHistory.RespondedAt = DateTime.Now;
                    assignmentHistory.Notes = request?.Reason ?? "Shipper rejected via API";
                    await _context.SaveChangesAsync();
                }

                // Cập nhật stats shipper
                var shipperProfile = await _context.ShipperProfiles
                    .FirstOrDefaultAsync(s => s.UserId == userId);
                if (shipperProfile != null)
                {
                    var activeOrders = await _context.Orders
                        .CountAsync(o => o.ShipperId == userId 
                            && (o.ShipperStatus == "Đã phân công" || o.ShipperStatus == "Đã xác nhận")
                            && o.Status != "Hoàn thành" 
                            && o.Status != "Đã hủy");
                    
                    shipperProfile.CurrentActiveOrders = activeOrders;
                    await _context.SaveChangesAsync();
                }

                // Gửi SignalR notification
                await _hubContext.Clients.All.SendAsync("ReceiveShipperUpdate", orderId, new
                {
                    orderId = order.OrderId,
                    shipperId = (string?)null,
                    shipperStatus = (string?)null,
                    message = "Shipper đã từ chối đơn hàng"
                });

                return Ok(new { 
                    success = true, 
                    message = "Đã từ chối đơn hàng. Hệ thống sẽ phân công cho shipper khác."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi từ chối đơn hàng.", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách đơn hàng ĐÃ HOÀN THÀNH của shipper
        /// </summary>
        [HttpGet("my-completed-deliveries")]
        public async Task<IActionResult> GetMyCompletedDeliveries()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var deliveries = await _context.Orders
                    .Where(o => o.ShipperId == userId && 
                            (o.Status == "Đã giao" || o.Status == "Hoàn thành"))
                    .Select(o => new ShipperLocationResponse
                    {
                        OrderId = o.Id,
                        OrderNumber = o.OrderId,
                        CustomerName = o.ReceiverName,
                        DeliveryAddress = o.ShippingAddress,
                        CurrentLatitude = o.LastKnownLatitude,
                        CurrentLongitude = o.LastKnownLongitude,
                        Status = o.Status,
                        ShipperStatus = o.ShipperStatus,
                        AssignedAt = o.AssignedAt,
                        DeliveryDate = o.DeliveryDate  // Thêm ngày hoàn thành
                    })
                    .OrderByDescending(o => o.DeliveryDate)  // Sắp xếp theo ngày giao gần nhất
                    .ToListAsync();

                return Ok(new { success = true, data = deliveries, count = deliveries.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server.", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy TOÀN BỘ lịch sử giao hàng của shipper (bao gồm cả thất bại, hủy...)
        /// </summary>
        [HttpGet("my-delivery-history")]
        public async Task<IActionResult> GetMyDeliveryHistory()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không tìm thấy thông tin người dùng." });

                var deliveries = await _context.Orders
                    .Where(o => o.ShipperId == userId)  // Lấy TẤT CẢ đơn của shipper
                    .Select(o => new ShipperLocationResponse
                    {
                        OrderId = o.Id,
                        OrderNumber = o.OrderId,
                        CustomerName = o.ReceiverName,
                        DeliveryAddress = o.ShippingAddress,
                        CurrentLatitude = o.LastKnownLatitude,
                        CurrentLongitude = o.LastKnownLongitude,
                        Status = o.Status,
                        ShipperStatus = o.ShipperStatus,
                        AssignedAt = o.AssignedAt,
                        DeliveryDate = o.DeliveryDate
                    })
                    .OrderByDescending(o => o.AssignedAt)  // Sắp xếp theo ngày phân công mới nhất
                    .ToListAsync();

                return Ok(new { success = true, data = deliveries, count = deliveries.Count });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server.", error = ex.Message });
            }
        }
    }
}