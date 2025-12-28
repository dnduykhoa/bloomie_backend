using Bloomie.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Extensions;
using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Bloomie.Hubs;
using Bloomie.Services;

namespace Bloomie.ApiControllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class OrderApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMomoService _momoService;
        private readonly IVNPAYService _vnpayService;
        private readonly IEmailService _emailService;
        private readonly IShippingService _shippingService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly INotificationService _notificationService;

        // Tỷ lệ quy đổi: 100 điểm = 10,000đ
        private const int POINTS_TO_VND = 100; // 100 điểm = 10,000đ (1 điểm = 100đ)

        public OrderApiController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IMomoService momoService,
            IVNPAYService vnpayService,
            IEmailService emailService,
            IShippingService shippingService,
            IHubContext<NotificationHub> hubContext,
            INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _momoService = momoService;
            _vnpayService = vnpayService;
            _emailService = emailService;
            _shippingService = shippingService;
            _hubContext = hubContext;
            _notificationService = notificationService;
        }

        // GET: api/OrderApi
        // Lấy danh sách đơn hàng của user hiện tại
        [HttpGet]
        public async Task<IActionResult> GetOrders([FromQuery] string? status)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var query = _context.Orders
                    .Where(o => o.UserId == userId)
                    .Include(o => o.OrderDetails!)
                        .ThenInclude(od => od.Product)
                            .ThenInclude(p => p!.Images)
                    .AsQueryable();

                // Lọc theo trạng thái nếu có
                if (!string.IsNullOrEmpty(status))
                {
                    query = query.Where(o => o.Status == status);
                }

                var orders = await query
                    .OrderByDescending(o => o.OrderDate)
                    .Select(o => new
                    {
                        o.Id,
                        o.OrderId,
                        o.OrderDate,
                        o.TotalAmount,
                        o.Status,
                        o.PaymentStatus,
                        o.PaymentMethod,
                        o.ReceiverName,
                        o.ShippingAddress,
                        o.Phone,
                        o.PointsUsed,
                        o.PromotionDiscount,
                        o.VoucherDiscount,
                        o.ShippingDiscount,
                        o.ShippingFee,
                        o.PointsDiscount,
                        o.CancelReason,
                        o.CancelledAt,
                        OrderDetails = o.OrderDetails!.Select(od => new
                        {
                            od.Id,
                            od.ProductId,
                            ProductName = od.Product!.Name,
                            od.Quantity,
                            od.UnitPrice,
                            OriginalPrice = od.Product!.Price,
                            DiscountAmount = od.Product.Price - od.UnitPrice,
                            TotalPrice = od.UnitPrice * od.Quantity,
                            ImageUrl = od.Product.ImageUrl ?? (od.Product.Images != null && od.Product.Images.Any() 
                                ? od.Product.Images.First().Url 
                                : "/images/placeholder.jpg"),
                            ProductImage = od.Product.ImageUrl,
                            Images = od.Product.Images!.Select(img => new { img.Id, img.Url }).ToList()
                        }).ToList()
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = orders
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: api/OrderApi/{id}
        // Lấy chi tiết đơn hàng
        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrderDetail(int id)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var order = await _context.Orders
                    .Include(o => o.OrderDetails!)
                        .ThenInclude(od => od.Product)
                            .ThenInclude(p => p!.Images)
                    .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

                if (order == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // Lấy thông tin hoàn trả nếu có
                var orderReturn = await _context.OrderReturns.FirstOrDefaultAsync(r => r.OrderId == id);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        order.Id,
                        order.OrderId,
                        order.OrderDate,
                        order.TotalAmount,
                        order.Status,
                        order.PaymentStatus,
                        order.PaymentMethod,
                        order.ReceiverName,
                        order.ShippingAddress,
                        order.Phone,
                        order.PointsUsed,
                        order.PromotionDiscount,
                        order.VoucherDiscount,
                        order.ShippingDiscount,
                        order.ShippingFee,
                        order.PointsDiscount,
                        order.CancelReason,
                        order.CancelledAt,
                        OrderDetails = order.OrderDetails!.Select(od => new
                        {
                            od.Id,
                            od.ProductId,
                            ProductName = od.Product!.Name,
                            od.Quantity,
                            od.UnitPrice,
                            OriginalPrice = od.Product!.Price,
                            DiscountAmount = od.Product.Price - od.UnitPrice,
                            TotalPrice = od.UnitPrice * od.Quantity,
                            od.DeliveryDate,
                            od.DeliveryTime,
                            od.Note,
                            ImageUrl = od.Product.ImageUrl ?? (od.Product.Images != null && od.Product.Images.Any() 
                                ? od.Product.Images.First().Url 
                                : "/images/placeholder.jpg"),
                            ProductImage = od.Product.ImageUrl,
                            Images = od.Product.Images!.Select(img => new { img.Id, img.Url }).ToList()
                        }).ToList(),
                        OrderReturn = orderReturn == null ? null : new
                        {
                            orderReturn.Id,
                            orderReturn.Reason,
                            orderReturn.ReturnType,
                            orderReturn.Status,
                            orderReturn.RequestDate,
                            orderReturn.Images
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // POST: api/OrderApi/checkout
        // Đặt hàng và thanh toán
        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.ShippingAddress))
                {
                    return BadRequest(new { success = false, message = "Vui lòng nhập địa chỉ giao hàng" });
                }

                if (string.IsNullOrWhiteSpace(request.Phone))
                {
                    return BadRequest(new { success = false, message = "Vui lòng nhập số điện thoại" });
                }

                if (string.IsNullOrWhiteSpace(request.PaymentMethod))
                {
                    return BadRequest(new { success = false, message = "Vui lòng chọn phương thức thanh toán" });
                }

                if (string.IsNullOrWhiteSpace(request.WardCode))
                {
                    return BadRequest(new { success = false, message = "Vui lòng chọn phường/xã giao hàng" });
                }

                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                // ⭐ LẤY GIỎ HÀNG TỪ DATABASE thay vì Session
                var dbCartItems = await _context.CartItems
                    .Include(c => c.Product)
                        .ThenInclude(p => p!.Images)
                    .Include(c => c.Product)
                        .ThenInclude(p => p!.ProductCategories)
                    .Where(c => c.UserId == userId)
                    .ToListAsync();

                if (!dbCartItems.Any())
                {
                    return BadRequest(new { success = false, message = "Giỏ hàng trống" });
                }

                // ⭐ Lấy cart state (voucher/promotion đã lưu)
                var cartState = await _context.UserCartStates
                    .FirstOrDefaultAsync(s => s.UserId == userId);

                // Chuyển sang ShoppingCart model để tương thích với logic cũ
                var sessionCart = new ShoppingCart
                {
                    CartItems = dbCartItems,
                    PromotionCode = cartState?.PromotionCode,
                    DiscountAmount = cartState?.DiscountAmount,
                    FreeShipping = cartState?.FreeShipping ?? false
                };

                // Kiểm tra tồn kho
                foreach (var item in sessionCart.CartItems)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product == null)
                    {
                        return BadRequest(new { success = false, message = $"Sản phẩm {item.Product?.Name} không tồn tại" });
                    }

                    if (!item.IsGift && product.StockQuantity < item.Quantity)
                    {
                        return BadRequest(new { success = false, message = $"Sản phẩm {product.Name} chỉ còn {product.StockQuantity} sản phẩm" });
                    }
                }

                // Tính toán tổng tiền SAU KHI ĐÃ TRỪ ProductDiscount
                // subtotal = tổng tiền sau khi đã áp dụng giảm giá sản phẩm
                decimal subtotal = sessionCart.CartItems.Sum(item =>
                {
                    var productPrice = item.Product?.Price ?? 0;
                    var productDiscount = item.Discount ?? 0;
                    var priceAfterProductDiscount = productPrice - productDiscount;
                    return priceAfterProductDiscount * item.Quantity;
                });

                // Xử lý voucher giảm giá từ ví user
                // ⭐ QUAN TRỌNG: 
                // - Nếu user chọn voucher từ ví → GHI ĐÈ promotion code từ cart
                // - Voucher được tính trên subtotal (đã trừ ProductDiscount)
                decimal voucherDiscount = 0;
                decimal promotionDiscount = 0;
                UserVoucher? selectedDiscountVoucher = null;
                
                if (request.SelectedDiscountVoucherId.HasValue && request.SelectedDiscountVoucherId.Value > 0)
                {
                    // User đã chọn voucher từ ví → Ưu tiên voucher từ ví, bỏ qua promotion từ cart
                    var now = DateTime.Now;
                    selectedDiscountVoucher = await _context.UserVouchers
                        .Include(uv => uv.PromotionCode)
                            .ThenInclude(pc => pc!.Promotion)
                                .ThenInclude(p => p!.PromotionGifts)
                        .FirstOrDefaultAsync(uv => uv.Id == request.SelectedDiscountVoucherId.Value
                            && uv.UserId == userId
                            && !uv.IsUsed
                            && uv.ExpiryDate > now);

                    if (selectedDiscountVoucher != null && selectedDiscountVoucher.PromotionCode != null)
                    {
                        var promotionCode = selectedDiscountVoucher.PromotionCode;
                        var promotion = promotionCode.Promotion;

                        // ⭐ KIỂM TRA THỜI GIAN PROMOTION
                        if (promotion != null)
                        {
                            if (!promotion.IsActive)
                            {
                                return BadRequest(new { success = false, message = "Chương trình khuyến mãi đã ngừng hoạt động" });
                            }

                            if (promotion.StartDate > now)
                            {
                                return BadRequest(new { success = false, message = "Chương trình khuyến mãi chưa bắt đầu" });
                            }

                            if (promotion.EndDate.HasValue && promotion.EndDate.Value < now)
                            {
                                return BadRequest(new { success = false, message = "Chương trình khuyến mãi đã kết thúc" });
                            }
                        }

                        // Kiểm tra MinOrderValue trên subtotal (sau ProductDiscount)
                        if (promotionCode.MinOrderValue.HasValue && subtotal < promotionCode.MinOrderValue.Value)
                        {
                            return BadRequest(new { success = false, message = $"Voucher giảm giá yêu cầu đơn hàng tối thiểu {promotionCode.MinOrderValue.Value:N0}đ" });
                        }

                        // ⭐ KIỂM TRA MinProductQuantity (Số lượng sản phẩm tối thiểu)
                        if (promotion?.MinProductQuantity.HasValue == true)
                        {
                            int totalProductQty = sessionCart.CartItems.Where(i => !i.IsGift).Sum(i => i.Quantity);
                            if (totalProductQty < promotion.MinProductQuantity.Value)
                            {
                                return BadRequest(new { success = false, message = $"Voucher giảm giá yêu cầu đơn hàng có tối thiểu {promotion.MinProductQuantity.Value} sản phẩm" });
                            }
                        }

                        // Xử lý voucher giảm giá (Order/Product/Shipping)
                        // Tính voucher discount trên subtotal (đã trừ ProductDiscount)
                        if (promotion?.Type != PromotionType.Gift)
                        {
                            if (promotionCode.IsPercent)
                            {
                                // Tính % trên subtotal (giá sau ProductDiscount)
                                voucherDiscount = (subtotal * (promotionCode.Value ?? 0)) / 100;
                            }
                            else
                            {
                                voucherDiscount = promotionCode.Value ?? 0;
                            }

                            // Áp dụng max discount nếu có
                            if (promotionCode.MaxDiscount.HasValue && voucherDiscount > promotionCode.MaxDiscount.Value)
                            {
                                voucherDiscount = promotionCode.MaxDiscount.Value;
                            }

                            // Đảm bảo discount không vượt quá subtotal
                            if (voucherDiscount > subtotal)
                            {
                                voucherDiscount = subtotal;
                            }
                        }
                    }
                }
                else
                {
                    // Không có voucher từ ví → Dùng promotion code từ cart (nếu có)
                    promotionDiscount = sessionCart.DiscountAmount ?? 0;
                }

                // Xử lý voucher vận chuyển
                decimal shippingVoucherDiscount = 0;
                UserVoucher? selectedShippingVoucher = null;
                if (request.SelectedShippingVoucherId.HasValue && request.SelectedShippingVoucherId.Value > 0)
                {
                    var now = DateTime.Now;
                    selectedShippingVoucher = await _context.UserVouchers
                        .Include(uv => uv.PromotionCode)
                            .ThenInclude(pc => pc!.Promotion)
                        .FirstOrDefaultAsync(uv => uv.Id == request.SelectedShippingVoucherId.Value
                            && uv.UserId == userId
                            && !uv.IsUsed
                            && uv.ExpiryDate > now);

                    if (selectedShippingVoucher != null && selectedShippingVoucher.PromotionCode != null)
                    {
                        var promotionCode = selectedShippingVoucher.PromotionCode;
                        var shippingPromotion = promotionCode.Promotion;

                        // ⭐ KIỂM TRA THỜI GIAN PROMOTION CHO SHIPPING VOUCHER
                        if (shippingPromotion != null)
                        {
                            if (!shippingPromotion.IsActive)
                            {
                                return BadRequest(new { success = false, message = "Chương trình khuyến mãi vận chuyển đã ngừng hoạt động" });
                            }

                            if (shippingPromotion.StartDate > now)
                            {
                                return BadRequest(new { success = false, message = "Chương trình khuyến mãi vận chuyển chưa bắt đầu" });
                            }

                            if (shippingPromotion.EndDate.HasValue && shippingPromotion.EndDate.Value < now)
                            {
                                return BadRequest(new { success = false, message = "Chương trình khuyến mãi vận chuyển đã kết thúc" });
                            }

                            // ⭐ KIỂM TRA MinProductQuantity CHO SHIPPING VOUCHER
                            if (shippingPromotion.MinProductQuantity.HasValue)
                            {
                                int totalProductQty = sessionCart.CartItems.Where(i => !i.IsGift).Sum(i => i.Quantity);
                                if (totalProductQty < shippingPromotion.MinProductQuantity.Value)
                                {
                                    return BadRequest(new { success = false, message = $"Voucher vận chuyển yêu cầu đơn hàng có tối thiểu {shippingPromotion.MinProductQuantity.Value} sản phẩm" });
                                }
                            }

                            // ⭐ KIỂM TRA ApplyDistricts (Voucher có áp dụng cho khu vực giao hàng không)
                            if (!string.IsNullOrEmpty(shippingPromotion.ApplyDistricts) && !string.IsNullOrEmpty(request.WardCode))
                            {
                                // Parse districts/wards từ JSON (có thể là tên hoặc code)
                                var applyAreas = System.Text.Json.JsonSerializer.Deserialize<List<string>>(shippingPromotion.ApplyDistricts);

                                if (applyAreas != null && applyAreas.Any())
                                {
                                    // Kiểm tra trực tiếp ward code trước
                                    if (!applyAreas.Contains(request.WardCode))
                                    {
                                        // Nếu không khớp trực tiếp, thử convert tên phường sang ward code
                                        var wardCodes = new List<string>();
                                        
                                        foreach (var area in applyAreas)
                                        {
                                            // Nếu là số → Là ward code
                                            if (area.All(char.IsDigit))
                                            {
                                                wardCodes.Add(area);
                                            }
                                            else
                                            {
                                                // Nếu là text → Là tên phường, query để lấy ward code
                                                var shippingFee = await _context.ShippingFees
                                                    .FirstOrDefaultAsync(sf => sf.WardName.Contains(area) && sf.IsActive);
                                                
                                                if (shippingFee != null)
                                                {
                                                    wardCodes.Add(shippingFee.WardCode);
                                                }
                                            }
                                        }

                                        // Kiểm tra lại sau khi convert
                                        if (!wardCodes.Contains(request.WardCode))
                                        {
                                            return BadRequest(new { success = false, message = "Voucher vận chuyển không áp dụng cho khu vực giao hàng này" });
                                        }
                                    }
                                }
                            }
                        }

                        // ⭐ KIỂM TRA KẾT HỢP giữa discount voucher và shipping voucher
                        if (selectedDiscountVoucher != null && selectedDiscountVoucher.PromotionCode?.Promotion != null && shippingPromotion != null)
                        {
                            var discountPromotion = selectedDiscountVoucher.PromotionCode.Promotion;
                            
                            // Kiểm tra discount voucher có cho phép kết hợp với shipping không
                            if (!discountPromotion.AllowCombineShipping)
                            {
                                return BadRequest(new { success = false, message = "Không thể sử dụng cả voucher giảm giá và voucher vận chuyển cùng lúc" });
                            }

                            // Kiểm tra ngược lại
                            bool shippingAllowCombine = false;
                            if (discountPromotion.Type == PromotionType.Order)
                            {
                                shippingAllowCombine = shippingPromotion.AllowCombineOrder;
                            }
                            else if (discountPromotion.Type == PromotionType.Product)
                            {
                                shippingAllowCombine = shippingPromotion.AllowCombineProduct;
                            }

                            if (!shippingAllowCombine)
                            {
                                return BadRequest(new { success = false, message = "Voucher vận chuyển không thể kết hợp với voucher giảm giá đang chọn" });
                            }
                        }

                        // Tính phí ship trước để tính discount
                        decimal? tempShippingFee = await _shippingService.GetShippingFee(request.WardCode);
                        if (tempShippingFee == null)
                        {
                            return BadRequest(new { success = false, message = "Phường/xã chưa hỗ trợ giao hàng" });
                        }

                        decimal baseFee = sessionCart.FreeShipping ? 0 : tempShippingFee.Value;

                        // Tính shipping discount
                        if (promotionCode.IsPercent)
                        {
                            shippingVoucherDiscount = (baseFee * (promotionCode.Value ?? 0)) / 100;
                        }
                        else
                        {
                            shippingVoucherDiscount = Math.Min(promotionCode.Value ?? 0, baseFee);
                        }

                        // Áp dụng max discount
                        if (promotionCode.MaxDiscount.HasValue && shippingVoucherDiscount > promotionCode.MaxDiscount.Value)
                        {
                            shippingVoucherDiscount = promotionCode.MaxDiscount.Value;
                        }

                        if (shippingVoucherDiscount > baseFee)
                        {
                            shippingVoucherDiscount = baseFee;
                        }
                    }
                }

                decimal totalDiscount = promotionDiscount + voucherDiscount;

                // Xử lý điểm tích lũy
                decimal pointsDiscount = 0;
                int actualPointsUsed = 0;
                if (request.PointsToUse.HasValue && request.PointsToUse.Value > 0)
                {
                    var userPoints = await _context.UserPoints.FirstOrDefaultAsync(up => up.UserId == userId);
                    if (userPoints == null || userPoints.TotalPoints < request.PointsToUse.Value)
                    {
                        return BadRequest(new { success = false, message = "Bạn không có đủ điểm để sử dụng" });
                    }

                    pointsDiscount = request.PointsToUse.Value * POINTS_TO_VND;

                    decimal maxDiscountAllowed = subtotal - totalDiscount + (sessionCart.FreeShipping ? 0 : (await _shippingService.GetShippingFee(request.WardCode) ?? 0));
                    if (pointsDiscount > maxDiscountAllowed)
                    {
                        pointsDiscount = maxDiscountAllowed;
                        actualPointsUsed = (int)(pointsDiscount / POINTS_TO_VND);
                    }
                    else
                    {
                        actualPointsUsed = request.PointsToUse.Value;
                    }
                }

                // Tính phí ship
                decimal? shippingFeeNullable = await _shippingService.GetShippingFee(request.WardCode);
                if (shippingFeeNullable == null)
                {
                    return BadRequest(new { success = false, message = "Phường/xã chưa hỗ trợ giao hàng" });
                }
                decimal shippingFeeOriginal = sessionCart.FreeShipping ? 0 : shippingFeeNullable.Value;

                // Tính phí ship sau khi áp dụng shipping voucher discount
                decimal shippingFeeAfterDiscount = shippingFeeOriginal - shippingVoucherDiscount;
                if (shippingFeeAfterDiscount < 0) shippingFeeAfterDiscount = 0;

                decimal totalAmount = subtotal - totalDiscount - pointsDiscount + shippingFeeAfterDiscount;

                // Tạo đơn hàng
                var order = new Order
                {
                    UserId = userId,
                    OrderDate = DateTime.Now,
                    TotalAmount = totalAmount,
                    Status = "Chờ xác nhận",
                    PaymentMethod = request.PaymentMethod,
                    PaymentStatus = request.PaymentMethod == "COD" ? "Chưa thanh toán" : "Chờ thanh toán",
                    ReceiverName = request.ReceiverName,
                    ShippingAddress = request.ShippingAddress,
                    Phone = request.Phone,
                    Note = request.Note,
                    PointsUsed = actualPointsUsed,
                    // Lưu thông tin giảm giá
                    PromotionDiscount = promotionDiscount,
                    VoucherDiscount = voucherDiscount,
                    ShippingDiscount = shippingVoucherDiscount,
                    PointsDiscount = pointsDiscount,
                    ShippingFee = shippingFeeOriginal, // ⭐ Lưu phí ship gốc (chưa trừ discount)
                    DiscountVoucherCode = selectedDiscountVoucher?.PromotionCode?.Code,
                    ShippingVoucherCode = selectedShippingVoucher?.PromotionCode?.Code,
                    OrderDetails = new List<OrderDetail>()
                };

                // Thêm chi tiết đơn hàng
                if (sessionCart.CartItems != null)
                {
                    foreach (var item in sessionCart.CartItems)
                    {
                        var product = await _context.Products.FindAsync(item.ProductId);
                        if (product == null) continue;

                        decimal unitPrice = product.Price - (item.Discount ?? 0);
                        if (item.Discount.HasValue && item.Discount.Value > 0)
                        {
                            unitPrice = product.Price - item.Discount.Value;
                        }

                        order.OrderDetails.Add(new OrderDetail
                        {
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            UnitPrice = unitPrice,
                            Note = item.Note,
                            DeliveryDate = item.DeliveryDate,
                            DeliveryTime = item.DeliveryTime
                        });
                    }
                }

                // Lưu đơn hàng
                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                // Sinh OrderId
                string randomStr = "";
                var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                var rand = new Random();
                for (int i = 0; i < 3; i++)
                    randomStr += chars[rand.Next(chars.Length)];
                string paddedId = order.Id.ToString().PadLeft(2, '0');
                order.OrderId = $"{DateTime.Now:yyMMdd}{paddedId}{randomStr}";

                // Nếu thanh toán online, đặt lịch tự động hủy sau 30 phút
                if (request.PaymentMethod == "Momo" || request.PaymentMethod == "VNPAY")
                {
                    var jobId = Bloomie.Services.Implementations.OrderCancellationService.ScheduleCancellation(order.Id);
                    order.CancellationJobId = jobId;
                }

                _context.Orders.Update(order);
                await _context.SaveChangesAsync();

                // 🔔 Gửi thông báo real-time cho Admin khi có đơn hàng mới
                try
                {
                    var user = await _userManager.GetUserAsync(User);
                    var customerName = user?.FullName ?? "Khách hàng";
                    var customerUsername = user?.UserName ?? "N/A";
                    
                    // Gửi thông báo text
                    await _notificationService.SendNotificationToAdmins(
                        $"🛒 Đơn hàng mới #{order.OrderId} từ {customerName} - Tổng: {order.TotalAmount:N0}đ",
                        $"/Admin/AdminOrder/Details/{order.Id}",
                        "success"
                    );

                    // 🔔 Gửi thông tin đơn hàng đầy đủ để hiển thị realtime trong table
                    await _hubContext.Clients.Group("AdminGroup").SendAsync("ReceiveNewOrder", new
                    {
                        id = order.Id,
                        orderId = order.OrderId,
                        customerUsername = customerUsername,
                        orderDate = order.OrderDate.ToString("dd/MM/yyyy HH:mm"),
                        status = order.Status,
                        paymentStatus = order.PaymentStatus,
                        totalAmount = order.TotalAmount
                    });
                }
                catch (Exception ex)
                {
                    // Log lỗi nhưng không ảnh hưởng đến flow đặt hàng
                    Console.WriteLine($"Lỗi gửi thông báo: {ex.Message}");
                }

                // Mark vouchers as used
                if (selectedDiscountVoucher != null)
                {
                    selectedDiscountVoucher.IsUsed = true;
                    selectedDiscountVoucher.UsedDate = DateTime.Now;
                    selectedDiscountVoucher.OrderId = order.Id;
                    _context.UserVouchers.Update(selectedDiscountVoucher);
                }

                if (selectedShippingVoucher != null)
                {
                    selectedShippingVoucher.IsUsed = true;
                    selectedShippingVoucher.UsedDate = DateTime.Now;
                    selectedShippingVoucher.OrderId = order.Id;
                    _context.UserVouchers.Update(selectedShippingVoucher);
                }

                if (selectedDiscountVoucher != null || selectedShippingVoucher != null)
                {
                    await _context.SaveChangesAsync();
                }

                // Trừ điểm nếu COD
                if (actualPointsUsed > 0 && request.PaymentMethod == "COD")
                {
                    var userPoints = await _context.UserPoints.FirstOrDefaultAsync(up => up.UserId == userId);
                    if (userPoints != null)
                    {
                        userPoints.TotalPoints -= actualPointsUsed;
                        userPoints.LastUpdated = DateTime.Now;
                        _context.UserPoints.Update(userPoints);

                        var pointHistory = new PointHistory
                        {
                            UserId = userId,
                            Points = -actualPointsUsed,
                            Reason = $"Sử dụng điểm cho đơn hàng {order.OrderId}",
                            CreatedDate = DateTime.Now,
                            OrderId = order.Id
                        };
                        _context.PointHistories.Add(pointHistory);
                        await _context.SaveChangesAsync();
                    }
                }

                // Cập nhật promotion code usage
                if (!string.IsNullOrEmpty(sessionCart.PromotionCode))
                {
                    var promoCode = await _context.PromotionCodes
                        .FirstOrDefaultAsync(pc => pc.Code == sessionCart.PromotionCode && pc.IsActive);

                    if (promoCode != null)
                    {
                        promoCode.UsedCount++;
                        await _context.SaveChangesAsync();
                    }
                }

                // ⭐ Xóa giỏ hàng từ DATABASE
                _context.CartItems.RemoveRange(dbCartItems);
                
                // ⭐ Xóa cart state
                if (cartState != null)
                {
                    _context.UserCartStates.Remove(cartState);
                }
                
                await _context.SaveChangesAsync();

                // Xử lý thanh toán online
                if (request.PaymentMethod == "Momo")
                {
                    var user = await _userManager.GetUserAsync(User);
                    var momoModel = new Bloomie.Models.Momo.OrderInfoModel
                    {
                        OrderId = order.OrderId,
                        FullName = user?.UserName ?? "Khách hàng",
                        Amount = (double)totalAmount,
                        OrderInformation = $"Thanh toán đơn hàng {order.OrderId}"
                    };

                    try
                    {
                        var momoResponse = await _momoService.CreatePaymentMomo(momoModel);
                        if (momoResponse != null && !string.IsNullOrEmpty(momoResponse.PayUrl))
                        {
                            return Ok(new
                            {
                                success = true,
                                message = "Đặt hàng thành công",
                                orderId = order.Id,
                                orderCode = order.OrderId,
                                paymentUrl = momoResponse.PayUrl
                            });
                        }
                        else
                        {
                            return Ok(new
                            {
                                success = true,
                                message = "Đặt hàng thành công nhưng không thể tạo link thanh toán Momo",
                                orderId = order.Id,
                                orderCode = order.OrderId
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        return Ok(new
                        {
                            success = true,
                            message = $"Đặt hàng thành công nhưng lỗi Momo: {ex.Message}",
                            orderId = order.Id,
                            orderCode = order.OrderId
                        });
                    }
                }
                else if (request.PaymentMethod == "VNPAY")
                {
                    var vnpayModel = new Bloomie.Models.Vnpay.PaymentInformationModel
                    {
                        OrderType = "billpayment",
                        Amount = order.TotalAmount,
                        OrderDescription = $"Thanh toán đơn hàng {order.OrderId}",
                        Name = order.ShippingAddress ?? "Khách hàng",
                        TxnRef = order.OrderId
                    };
                    var paymentUrl = _vnpayService.CreatePaymentUrl(vnpayModel, HttpContext);
                    if (!string.IsNullOrEmpty(paymentUrl))
                    {
                        return Ok(new
                        {
                            success = true,
                            message = "Đặt hàng thành công",
                            orderId = order.Id,
                            orderCode = order.OrderId,
                            paymentUrl = paymentUrl
                        });
                    }
                    else
                    {
                        return Ok(new
                        {
                            success = true,
                            message = "Đặt hàng thành công nhưng không thể tạo link thanh toán VNPAY",
                            orderId = order.Id,
                            orderCode = order.OrderId
                        });
                    }
                }
                else // COD
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Đặt hàng thành công",
                        orderId = order.Id,
                        orderCode = order.OrderId
                    });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message,
                });
            }
        }

        // POST: api/OrderApi/{id}/cancel
        // Hủy đơn hàng
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelOrder(int id, [FromBody] CancelOrderRequest request)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var order = await _context.Orders
                    .Include(o => o.OrderDetails!)
                        .ThenInclude(od => od.Product)
                    .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

                if (order == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // Chỉ cho phép hủy đơn hàng ở trạng thái "Chờ xác nhận"
                if (order.Status != "Chờ xác nhận" && order.Status != "Chờ thanh toán" && order.PaymentStatus != "Đã thanh toán")
                {
                    return BadRequest(new { success = false, message = "Không thể hủy đơn hàng ở trạng thái hiện tại" });
                }

                // Lưu lý do hủy và thời gian hủy
                order.Status = "Đã hủy";
                order.CancelReason = request.CancelReason;
                order.CancelledAt = DateTime.Now;
                
                await _context.SaveChangesAsync();

                // Gửi email xác nhận hủy
                await SendOrderCancelledByCustomerEmailAsync(order, request.CancelReason);

                return Ok(new
                {
                    success = true,
                    message = "Đã hủy đơn hàng thành công"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // POST: api/OrderApi/{id}/confirm-received
        // Xác nhận đã nhận hàng
        [HttpPost("{id}/confirm-received")]
        public async Task<IActionResult> ConfirmReceived(int id)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

                if (order == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // Chỉ cho phép xác nhận khi đơn hàng đã giao
                if (order.Status != "Đã giao")
                {
                    return BadRequest(new { success = false, message = "Không thể xác nhận ở trạng thái hiện tại" });
                }

                order.Status = "Hoàn thành";
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã xác nhận nhận hàng thành công"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: api/OrderApi/{id}/track
        // Theo dõi đơn hàng
        [HttpGet("{id}/track")]
        public async Task<IActionResult> TrackOrder(int id)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var order = await _context.Orders
                    .Include(o => o.OrderDetails!)
                        .ThenInclude(od => od.Product)
                            .ThenInclude(p => p!.Images)
                    .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

                if (order == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // Lấy thông tin hoàn trả nếu có
                var orderReturn = await _context.OrderReturns.FirstOrDefaultAsync(r => r.OrderId == id);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        order.Id,
                        order.OrderId,
                        order.OrderDate,
                        order.TotalAmount,
                        order.Status,
                        order.PaymentStatus,
                        order.PaymentMethod,
                        order.ReceiverName,
                        order.ShippingAddress,
                        order.Phone,
                        order.CancelReason,
                        order.CancelledAt,
                        OrderDetails = order.OrderDetails!.Select(od => new
                        {
                            od.Id,
                            od.ProductId,
                            ProductName = od.Product!.Name,
                            od.Quantity,
                            od.UnitPrice,
                            OriginalPrice = od.Product!.Price,
                            DiscountAmount = od.Product.Price - od.UnitPrice,
                            TotalPrice = od.UnitPrice * od.Quantity,
                            od.DeliveryDate,
                            od.DeliveryTime,
                            ImageUrl = od.Product.ImageUrl ?? (od.Product.Images != null && od.Product.Images.Any() 
                                ? od.Product.Images.First().Url 
                                : "/images/placeholder.jpg"),
                            ProductImage = od.Product.ImageUrl,
                            Images = od.Product.Images!.Select(img => new { img.Id, img.Url }).ToList()
                        }).ToList(),
                        OrderReturn = orderReturn == null ? null : new
                        {
                            orderReturn.Id,
                            orderReturn.Reason,
                            orderReturn.ReturnType,
                            orderReturn.Status,
                            orderReturn.RequestDate
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // POST: api/OrderApi/{id}/reorder
        // Đặt lại đơn hàng
        [HttpPost("{id}/reorder")]
        public async Task<IActionResult> Reorder(int id)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var order = await _context.Orders
                    .Include(o => o.OrderDetails!)
                        .ThenInclude(od => od.Product)
                    .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

                if (order == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // ⭐ Lấy giỏ hàng từ DATABASE
                var dbCartItems = await _context.CartItems
                    .Include(c => c.Product)
                        .ThenInclude(p => p!.Images)
                    .Where(c => c.UserId == userId)
                    .ToListAsync();

                foreach (var detail in order.OrderDetails ?? new List<OrderDetail>())
                {
                    if (detail.Product != null)
                    {
                        // Kiểm tra tồn kho
                        if (detail.Product.StockQuantity < detail.Quantity)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message = $"Sản phẩm {detail.Product.Name} chỉ còn {detail.Product.StockQuantity} sản phẩm"
                            });
                        }

                        // ⭐ Thêm vào database
                        var existingItem = dbCartItems.FirstOrDefault(c => c.ProductId == detail.ProductId);
                        if (existingItem != null)
                        {
                            existingItem.Quantity += detail.Quantity;
                            _context.CartItems.Update(existingItem);
                        }
                        else
                        {
                            var newItem = new CartItem
                            {
                                UserId = userId,
                                ProductId = detail.ProductId,
                                Quantity = detail.Quantity,
                                IsGift = false
                            };
                            _context.CartItems.Add(newItem);
                        }
                    }
                }

                // Lưu vào database
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã thêm sản phẩm từ đơn hàng cũ vào giỏ hàng"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // POST: api/OrderApi/{id}/request-return
        // Yêu cầu đổi trả hàng
        [HttpPost("{id}/request-return")]
        public async Task<IActionResult> RequestReturn(int id, [FromBody] RequestReturnRequest request)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

                if (order == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // Chỉ cho phép đổi trả với đơn hàng đã giao hoặc hoàn thành
                if (order.Status != "Đã giao" && order.Status != "Hoàn thành")
                {
                    return BadRequest(new { success = false, message = "Chỉ có thể yêu cầu đổi trả với đơn hàng đã giao" });
                }

                // Kiểm tra đã có yêu cầu đổi trả chưa
                var existingReturn = await _context.OrderReturns
                    .FirstOrDefaultAsync(r => r.OrderId == id);

                if (existingReturn != null)
                {
                    return BadRequest(new { success = false, message = "Đơn hàng này đã có yêu cầu đổi trả" });
                }

                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return BadRequest(new { success = false, message = "Vui lòng nhập lý do đổi trả" });
                }

                // Tạo yêu cầu đổi trả
                var orderReturn = new OrderReturn
                {
                    OrderId = id,
                    Reason = request.Reason,
                    ReturnType = request.ReturnType ?? "Đổi hàng",
                    Status = "Chờ xử lý",
                    RequestDate = DateTime.Now,
                    Images = request.Images != null ? string.Join(";", request.Images) : null
                };

                _context.OrderReturns.Add(orderReturn);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã gửi yêu cầu đổi trả thành công"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: api/OrderApi/shipping-fee
        // Lấy phí ship theo ward code
        [AllowAnonymous]
        [HttpGet("shipping-fee")]
        public async Task<IActionResult> GetShippingFee([FromQuery] string wardCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(wardCode))
                {
                    return BadRequest(new { success = false, message = "Vui lòng chọn phường/xã" });
                }

                var fee = await _shippingService.GetShippingFee(wardCode);
                if (fee == null)
                {
                    return BadRequest(new { success = false, message = "Phường/xã này chưa hỗ trợ giao hàng" });
                }

                return Ok(new
                {
                    success = true,
                    fee = fee
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // POST: api/OrderApi/{id}/retry-payment
        // Thanh toán lại đơn hàng
        [HttpPost("{id}/retry-payment")]
        public async Task<IActionResult> RetryPayment(int id)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

                if (order == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // Chỉ cho phép thanh toán lại nếu PaymentStatus là "Chờ thanh toán"
                if (order.PaymentStatus != "Chờ thanh toán")
                {
                    return BadRequest(new { success = false, message = "Đơn hàng này không thể thanh toán lại" });
                }

                // Gọi lại service thanh toán
                if (order.PaymentMethod == "Momo")
                {
                    var user = await _userManager.GetUserAsync(User);
                    var newMomoOrderId = $"{order.OrderId}_{DateTime.UtcNow.Ticks}";

                    var momoModel = new Bloomie.Models.Momo.OrderInfoModel
                    {
                        OrderId = newMomoOrderId,
                        FullName = user?.UserName ?? "Khách hàng",
                        Amount = (double)order.TotalAmount,
                        OrderInformation = $"Thanh toán lại đơn hàng {order.OrderId}"
                    };

                    try
                    {
                        var momoResponse = await _momoService.CreatePaymentMomo(momoModel);
                        if (momoResponse != null && !string.IsNullOrEmpty(momoResponse.PayUrl))
                        {
                            return Ok(new
                            {
                                success = true,
                                message = "Tạo link thanh toán thành công",
                                paymentUrl = momoResponse.PayUrl
                            });
                        }
                        else
                        {
                            return BadRequest(new { success = false, message = "Không tạo được link thanh toán Momo" });
                        }
                    }
                    catch (Exception ex)
                    {
                        return BadRequest(new { success = false, message = $"Lỗi Momo: {ex.Message}" });
                    }
                }
                else if (order.PaymentMethod == "VNPAY")
                {
                    var vnpayModel = new Bloomie.Models.Vnpay.PaymentInformationModel
                    {
                        OrderType = "billpayment",
                        Amount = order.TotalAmount,
                        OrderDescription = $"Thanh toán lại đơn hàng {order.OrderId}",
                        Name = order.ShippingAddress ?? "Khách hàng",
                        TxnRef = order.OrderId
                    };

                    var paymentUrl = _vnpayService.CreatePaymentUrl(vnpayModel, HttpContext);
                    if (!string.IsNullOrEmpty(paymentUrl))
                    {
                        return Ok(new
                        {
                            success = true,
                            message = "Tạo link thanh toán thành công",
                            paymentUrl = paymentUrl
                        });
                    }
                    else
                    {
                        return BadRequest(new { success = false, message = "Không tạo được link thanh toán VNPAY" });
                    }
                }

                return BadRequest(new { success = false, message = "Phương thức thanh toán không được hỗ trợ" });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // Helper method - Gửi email hủy đơn hàng
        private async Task SendOrderCancelledByCustomerEmailAsync(Order order, string? cancelReason)
        {
            var user = await _context.Users.FindAsync(order.UserId);
            var email = user?.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var subject = $"[Bloomie] Xác nhận hủy đơn hàng #{order.OrderId}";
                var reasonText = !string.IsNullOrEmpty(cancelReason)
                    ? $"<strong>Lý do hủy:</strong> {cancelReason}<br/>"
                    : "";
                var body = $@"
                <!DOCTYPE html>
                <html lang='vi'>
                <head>
                    <meta charset='UTF-8'>
                    <style>
                        body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
                        .container {{ max-width: 600px; margin: 30px auto; background-color: #fff; border-radius: 10px; box-shadow: 0 4px 12px rgba(0,0,0,0.08); overflow: hidden; }}
                        .header {{ background-color: #6c757d; padding: 24px; text-align: center; }}
                        .header h1 {{ color: #fff; margin: 0; font-size: 28px; }}
                        .content {{ padding: 32px; color: #333; }}
                        .order-info {{ background-color: #f8f9fa; padding: 18px; border-radius: 6px; margin: 18px 0; }}
                        .footer {{ background-color: #f8f8f8; padding: 18px; text-align: center; font-size: 15px; color: #777; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Bloomie Flower Shop</h1>
                        </div>
                        <div class='content'>
                            <h2>Xác nhận hủy đơn hàng</h2>
                            <div class='order-info'>
                                <strong>Mã đơn hàng:</strong> #{order.OrderId}<br/>
                                <strong>Thời gian hủy:</strong> {DateTime.Now:HH:mm dd/MM/yyyy}<br/>
                                {reasonText}
                                <strong>Tổng tiền:</strong> {order.TotalAmount:N0} VNĐ<br/>
                            </div>
                            <p>Chúng tôi đã nhận được yêu cầu hủy đơn hàng của bạn và đã xử lý thành công.</p>
                        </div>
                        <div class='footer'>
                            <p>© 2025 Bloomie Flower Shop</p>
                        </div>
                    </div>
                </body>
                </html>
                ";
                await _emailService.SendEmailAsync(email, subject, body);
            }
        }

        // ===== CHAT SUPPORT ENDPOINTS =====
        
        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchOrders([FromQuery] string q = "", [FromQuery] string? userId = null)
        {
            try
            {
                var ordersQuery = _context.Orders.AsQueryable();

                // QUAN TRỌNG: Lọc theo userId nếu được cung cấp
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    ordersQuery = ordersQuery.Where(o => o.UserId == userId);
                }

                // Nếu có query thì search, không thì lấy tất cả
                if (!string.IsNullOrWhiteSpace(q))
                {
                    ordersQuery = ordersQuery.Where(o => 
                        (o.OrderId != null && o.OrderId.Contains(q)) ||
                        (o.ShippingAddress != null && o.ShippingAddress.Contains(q)) || 
                        (o.Phone != null && o.Phone.Contains(q)));
                }

                var orders = await ordersQuery
                    .OrderByDescending(o => o.OrderDate)
                    .Take(10)
                    .Select(o => new
                    {
                        id = o.Id,  // ID thực (int) để gọi API
                        orderId = o.OrderId,  // OrderId hiển thị (string)
                        customerName = o.ShippingAddress,
                        orderDate = o.OrderDate,
                        totalAmount = o.TotalAmount,
                        orderStatus = o.Status
                    })
                    .ToListAsync();

                return Ok(orders);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi tìm kiếm đơn hàng", error = ex.Message });
            }
        }

        [HttpGet("chat/{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetOrderById(int id)
        {
            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderDetails!)
                        .ThenInclude(od => od.Product!)
                    .Where(o => o.Id == id)
                    .FirstOrDefaultAsync();

                if (order == null)
                {
                    return NotFound(new { message = "Không tìm thấy đơn hàng" });
                }

                // Lấy ImageUrl (ảnh chính) của sản phẩm đầu tiên
                var firstProductImage = order.OrderDetails?.FirstOrDefault()?.Product?.ImageUrl;

                return Ok(new
                {
                    orderId = order.OrderId,
                    customerName = order.ShippingAddress,
                    orderDate = order.OrderDate,
                    totalAmount = order.TotalAmount,
                    orderStatus = order.Status,
                    imageUrl = firstProductImage
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy thông tin đơn hàng", error = ex.Message });
            }
        }

        // POST: api/OrderApi/confirm-payment
        // Xác nhận thanh toán từ Flutter sau khi nhận callback từ VNPAY
        [HttpPost("confirm-payment")]
        [Authorize]
        public async Task<IActionResult> ConfirmPayment([FromBody] ConfirmPaymentRequest request)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                // Tìm order theo orderId (string) hoặc Id (int)
                Order? order = null;
                if (!string.IsNullOrEmpty(request.OrderId))
                {
                    order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == request.OrderId && o.UserId == userId);
                }
                else if (request.Id.HasValue)
                {
                    order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == request.Id.Value && o.UserId == userId);
                }

                if (order == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đơn hàng" });
                }

                // Kiểm tra payment data
                if (request.PaymentData == null)
                {
                    return BadRequest(new { success = false, message = "Thiếu thông tin thanh toán" });
                }

                var paymentData = request.PaymentData;

                // Verify payment method
                if (request.PaymentMethod != order.PaymentMethod)
                {
                    return BadRequest(new { success = false, message = "Phương thức thanh toán không khớp" });
                }

                // Kiểm tra responseCode
                if (paymentData.ResponseCode != "00")
                {
                    // Thanh toán thất bại
                    order.PaymentStatus = "Chờ thanh toán";
                    _context.Orders.Update(order);
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        success = false,
                        message = $"Thanh toán thất bại. Mã lỗi: {paymentData.ResponseCode}",
                        paymentStatus = order.PaymentStatus
                    });
                }

                // Thanh toán thành công
                order.PaymentStatus = "Đã thanh toán";

                // Hủy job tự động hủy đơn hàng nếu có
                if (!string.IsNullOrEmpty(order.CancellationJobId))
                {
                    Bloomie.Services.Implementations.OrderCancellationService.CancelScheduledJob(order.CancellationJobId);
                    order.CancellationJobId = null;
                }

                _context.Orders.Update(order);

                // Trừ điểm nếu có sử dụng (cho thanh toán online)
                if (order.PointsUsed > 0)
                {
                    var userPoints = await _context.UserPoints.FirstOrDefaultAsync(up => up.UserId == userId);
                    if (userPoints != null)
                    {
                        userPoints.TotalPoints -= order.PointsUsed;
                        userPoints.LastUpdated = DateTime.Now;
                        _context.UserPoints.Update(userPoints);

                        var pointHistory = new PointHistory
                        {
                            UserId = userId,
                            Points = -order.PointsUsed,
                            Reason = $"Sử dụng điểm cho đơn hàng {order.OrderId}",
                            CreatedDate = DateTime.Now,
                            OrderId = order.Id
                        };
                        _context.PointHistories.Add(pointHistory);
                    }
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Xác nhận thanh toán thành công",
                    orderId = order.Id,
                    paymentStatus = order.PaymentStatus,
                    orderStatus = order.Status
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi xác nhận thanh toán", error = ex.Message });
            }
        }

        // GET: api/OrderApi/available-vouchers
        // Lấy danh sách voucher khả dụng của user
        [HttpGet("available-vouchers")]
        public async Task<IActionResult> GetAvailableVouchers()
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var now = DateTime.Now;
                var vouchers = await _context.UserVouchers
                    .Include(uv => uv.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                            .ThenInclude(p => p!.PromotionGifts)
                    .Where(uv => uv.UserId == userId 
                        && !uv.IsUsed 
                        && uv.ExpiryDate >= now
                        && uv.PromotionCode != null
                        && uv.PromotionCode.Promotion != null
                        && uv.PromotionCode.Promotion.IsActive
                        && uv.PromotionCode.Promotion.StartDate <= now
                        && (uv.PromotionCode.Promotion.EndDate == null || uv.PromotionCode.Promotion.EndDate >= now))
                    .OrderBy(uv => uv.ExpiryDate)
                    .ToListAsync();

                var result = vouchers.Select(v => new
                {
                    v.Id,
                    Code = v.PromotionCode?.Code,
                    v.Source,
                    v.CollectedDate,
                    v.ExpiryDate,
                    Promotion = new
                    {
                        v.PromotionCode?.Promotion?.Id,
                        v.PromotionCode?.Promotion?.Name,
                        v.PromotionCode?.Promotion?.Description,
                        v.PromotionCode?.Promotion?.Type,
                        v.PromotionCode?.Promotion?.StartDate,
                        v.PromotionCode?.Promotion?.EndDate,
                        ShippingDiscountType = v.PromotionCode?.Promotion?.ShippingDiscountType,
                        ShippingDiscountValue = v.PromotionCode?.Promotion?.ShippingDiscountValue,
                        ApplyDistricts = v.PromotionCode?.Promotion?.ApplyDistricts,
                        AllowCombineOrder = v.PromotionCode?.Promotion?.AllowCombineOrder ?? false,
                        AllowCombineProduct = v.PromotionCode?.Promotion?.AllowCombineProduct ?? false,
                        AllowCombineShipping = v.PromotionCode?.Promotion?.AllowCombineShipping ?? false,
                    },
                    VoucherInfo = new
                    {
                        IsPercent = v.PromotionCode?.IsPercent ?? false,
                        Value = v.PromotionCode?.Value,
                        MaxDiscount = v.PromotionCode?.MaxDiscount,
                        MinOrderValue = v.PromotionCode?.MinOrderValue,
                        MinProductQuantity = v.PromotionCode?.Promotion?.MinProductQuantity,
                        UsageLimit = v.PromotionCode?.UsageLimit,
                        UsedCount = v.PromotionCode?.UsedCount ?? 0,
                        LimitPerCustomer = v.PromotionCode?.LimitPerCustomer ?? false,
                    },
                    // ⭐ Thêm thông tin chi tiết về discount
                    DiscountInfo = GetVoucherDiscountInfo(v),
                    DisplayText = GetVoucherDisplayText(v)
                }).ToList();

                return Ok(new
                {
                    success = true,
                    data = result,
                    count = result.Count
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // Helper method để lấy thông tin chi tiết về discount
        private object GetVoucherDiscountInfo(UserVoucher voucher)
        {
            var promotion = voucher.PromotionCode?.Promotion;
            
            if (promotion?.Type == PromotionType.Gift)
            {
                var giftPromo = promotion.PromotionGifts?.FirstOrDefault();
                if (giftPromo != null)
                {
                    return new
                    {
                        Type = "gift",
                        DiscountType = giftPromo.GiftDiscountType, // "free", "percent", "money"
                        DiscountValue = giftPromo.GiftDiscountValue, // % nếu là percent
                        DiscountAmount = giftPromo.GiftDiscountMoneyValue, // số tiền nếu là money
                        IsFree = giftPromo.GiftDiscountType == "free",
                        DisplayText = giftPromo.GiftDiscountType == "free" ? "Miễn phí" :
                                     giftPromo.GiftDiscountType == "percent" && giftPromo.GiftDiscountValue.HasValue ? $"Giảm {giftPromo.GiftDiscountValue}%" :
                                     giftPromo.GiftDiscountType == "money" && giftPromo.GiftDiscountMoneyValue.HasValue ? $"Giảm {giftPromo.GiftDiscountMoneyValue:N0}₫" : "Tặng quà"
                    };
                }
                return new { Type = "gift", DisplayText = "Tặng quà" };
            }
            else if (promotion?.Type == PromotionType.Shipping)
            {
                // Voucher vận chuyển
                var isPercent = voucher.PromotionCode?.IsPercent ?? false;
                var value = voucher.PromotionCode?.Value ?? 0;
                var maxDiscount = voucher.PromotionCode?.MaxDiscount;
                
                return new
                {
                    Type = "shipping",
                    IsPercent = isPercent,
                    Value = value,
                    MaxDiscount = maxDiscount,
                    IsFree = promotion.ShippingDiscountType == "free" || (value == 100 && isPercent),
                    DisplayText = promotion.ShippingDiscountType == "free" ? "Miễn phí vận chuyển" :
                                 isPercent ? (maxDiscount.HasValue ? $"Giảm {value}% phí ship (tối đa {maxDiscount.Value:N0}₫)" : $"Giảm {value}% phí ship") :
                                 $"Giảm {value:N0}₫ phí ship"
                };
            }
            else
            {
                // Voucher giảm giá đơn hàng/sản phẩm
                var isPercent = voucher.PromotionCode?.IsPercent ?? false;
                var value = voucher.PromotionCode?.Value ?? 0;
                var maxDiscount = voucher.PromotionCode?.MaxDiscount;
                
                return new
                {
                    Type = promotion?.Type == PromotionType.Order ? "order" : "product",
                    IsPercent = isPercent,
                    Value = value,
                    MaxDiscount = maxDiscount,
                    IsFree = false,
                    DisplayText = isPercent ? (maxDiscount.HasValue ? $"Giảm {value}% (tối đa {maxDiscount.Value:N0}₫)" : $"Giảm {value}%") :
                                 $"Giảm {value:N0}₫"
                };
            }
        }

        // Helper method để tạo text hiển thị voucher
        private string GetVoucherDisplayText(UserVoucher voucher)
        {
            var promotion = voucher.PromotionCode?.Promotion;
            var code = voucher.PromotionCode?.Code ?? "";
            
            if (promotion?.Type == PromotionType.Gift)
            {
                var giftPromo = promotion.PromotionGifts?.FirstOrDefault();
                if (giftPromo != null)
                {
                    if (giftPromo.GiftDiscountType == "free")
                    {
                        return $"{code} - Tặng miễn phí";
                    }
                    else if (giftPromo.GiftDiscountType == "percent" && giftPromo.GiftDiscountValue.HasValue)
                    {
                        return $"{code} - Giảm {giftPromo.GiftDiscountValue}% sản phẩm tặng";
                    }
                    else if (giftPromo.GiftDiscountType == "money" && giftPromo.GiftDiscountMoneyValue.HasValue)
                    {
                        return $"{code} - Giảm {giftPromo.GiftDiscountMoneyValue:N0}₫ sản phẩm tặng";
                    }
                }
                return $"{code} - Tặng quà";
            }
            else if (promotion?.Type == PromotionType.Shipping)
            {
                // Voucher vận chuyển
                if (promotion.ShippingDiscountType == "free")
                {
                    return $"{code} - Miễn phí vận chuyển";
                }
                else if (voucher.PromotionCode?.IsPercent == true)
                {
                    var text = $"{code} - Giảm {voucher.PromotionCode.Value}% phí ship";
                    if (voucher.PromotionCode.MaxDiscount.HasValue)
                    {
                        text += $" (tối đa {voucher.PromotionCode.MaxDiscount.Value:N0}₫)";
                    }
                    return text;
                }
                else if (voucher.PromotionCode?.Value.HasValue == true)
                {
                    return $"{code} - Giảm {voucher.PromotionCode.Value.Value:N0}₫ phí ship";
                }
                return $"{code} - Voucher vận chuyển";
            }
            else if (voucher.PromotionCode?.IsPercent == true)
            {
                var text = $"{code} - Giảm {voucher.PromotionCode.Value}%";
                if (voucher.PromotionCode.MaxDiscount.HasValue)
                {
                    text += $" (tối đa {voucher.PromotionCode.MaxDiscount.Value:N0}đ)";
                }
                return text;
            }
            else if (voucher.PromotionCode?.Value.HasValue == true)
            {
                return $"{code} - Giảm {voucher.PromotionCode.Value.Value:N0}₫";
            }
            
            return code;
        }

        // POST: api/OrderApi/apply-voucher
        // Áp dụng voucher vào giỏ hàng
        [HttpPost("apply-voucher")]
        public async Task<IActionResult> ApplyVoucher([FromBody] ApplyVoucherRequest request)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                // ⭐ Lấy giỏ hàng từ DATABASE
                var dbCartItems = await _context.CartItems
                    .Include(c => c.Product)
                        .ThenInclude(p => p!.Images)
                    .Include(c => c.Product)
                        .ThenInclude(p => p!.ProductCategories)
                    .Where(c => c.UserId == userId)
                    .ToListAsync();
                
                if (!dbCartItems.Any())
                {
                    return BadRequest(new { success = false, message = "Giỏ hàng trống" });
                }

                // ⭐ Lấy cart state
                var cartState = await _context.UserCartStates
                    .FirstOrDefaultAsync(s => s.UserId == userId);

                if (cartState == null)
                {
                    cartState = new UserCartState { UserId = userId };
                    _context.UserCartStates.Add(cartState);
                }

                // Convert sang ShoppingCart để giữ logic cũ
                var cart = new ShoppingCart
                {
                    CartItems = dbCartItems,
                    PromotionCode = cartState.PromotionCode,
                    DiscountAmount = cartState.DiscountAmount,
                    FreeShipping = cartState.FreeShipping
                };
                
                // ⭐ THÊM: Tính lại discount từ database trước khi tính voucher
                var now = DateTime.Now;
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .ToListAsync();
                
                foreach (var item in cart.CartItems)
                {
                    // ⭐ Tính lại discount từ product nếu item.Discount null hoặc 0
                    if (item.Product != null && !item.IsGift)
                    {
                        if (!item.Discount.HasValue || item.Discount.Value == 0)
                        {
                            decimal? itemDiscount = null;
                            
                            foreach (var discount in activeDiscounts)
                            {
                                bool isApplicable = false;
                                
                                if (discount.ApplyTo == "all")
                                {
                                    isApplicable = true;
                                }
                                else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                                {
                                    var discountProductIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                                    if (discountProductIds != null && discountProductIds.Contains(item.ProductId))
                                    {
                                        isApplicable = true;
                                    }
                                }
                                
                                if (isApplicable)
                                {
                                    decimal tempDiscount = 0;
                                    
                                    if (discount.DiscountType == "percent")
                                    {
                                        tempDiscount = item.Product.Price * (discount.DiscountValue / 100);
                                    }
                                    else if (discount.DiscountType == "fixed_amount")
                                    {
                                        tempDiscount = discount.DiscountValue;
                                    }
                                    
                                    if (tempDiscount > (itemDiscount ?? 0))
                                    {
                                        itemDiscount = tempDiscount;
                                    }
                                }
                            }
                            
                            item.Discount = itemDiscount;
                        }
                    }
                }

                if (request.VoucherId == null || request.VoucherId == 0)
                {
                    // Xóa voucher hiện tại
                    cart.PromotionCode = null;
                    cart.DiscountAmount = 0;
                    cart.FreeShipping = false;
                    
                    // Xóa gift items từ DATABASE
                    var giftItems = dbCartItems.Where(i => i.IsGift).ToList();
                    if (giftItems.Any())
                    {
                        _context.CartItems.RemoveRange(giftItems);
                    }
                    
                    // Cập nhật cart state
                    cartState.PromotionCode = null;
                    cartState.DiscountAmount = 0;
                    cartState.FreeShipping = false;
                    await _context.SaveChangesAsync();
                    
                    return Ok(new
                    {
                        success = true,
                        message = "Đã xóa voucher",
                        discountAmount = 0m,
                        shippingDiscount = 0m,
                        hasGiftItems = false,
                        giftItems = new List<object>()
                    });
                }

                // Tìm voucher
                var userVoucher = await _context.UserVouchers
                    .Include(uv => uv.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                            .ThenInclude(p => p!.PromotionGifts!)
                                .ThenInclude(pg => pg.BuyProducts)
                    .Include(uv => uv.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                            .ThenInclude(p => p!.PromotionGifts!)
                                .ThenInclude(pg => pg.BuyCategories)
                    .Include(uv => uv.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                            .ThenInclude(p => p!.PromotionGifts!)
                                .ThenInclude(pg => pg.GiftProducts!)
                                    .ThenInclude(gp => gp.Product)
                    .FirstOrDefaultAsync(uv => uv.Id == request.VoucherId && uv.UserId == userId);

                if (userVoucher == null)
                {
                    return BadRequest(new { success = false, message = $"Voucher ID {request.VoucherId} không tồn tại hoặc không thuộc về user {userId}" });
                }

                if (userVoucher.IsUsed)
                {
                    return BadRequest(new { success = false, message = $"Voucher {userVoucher.PromotionCode?.Code} đã được sử dụng vào {userVoucher.UsedDate:dd/MM/yyyy HH:mm}" });
                }

                if (userVoucher.ExpiryDate < DateTime.Now)
                {
                    return BadRequest(new { success = false, message = $"Voucher {userVoucher.PromotionCode?.Code} đã hết hạn vào {userVoucher.ExpiryDate:dd/MM/yyyy HH:mm}" });
                }

                var promotion = userVoucher.PromotionCode?.Promotion;
                if (promotion == null)
                {
                    return BadRequest(new { success = false, message = $"Voucher {userVoucher.PromotionCode?.Code} không có promotion liên kết" });
                }
                
                if (!promotion.IsActive)
                {
                    return BadRequest(new { success = false, message = $"Promotion {promotion.Name} đã bị vô hiệu hóa" });
                }
                
                // ⭐ KIỂM TRA THỜI GIAN PROMOTION  
                if (promotion.StartDate > DateTime.Now)
                {
                    return BadRequest(new { success = false, message = $"Promotion {promotion.Name} chưa bắt đầu (bắt đầu: {promotion.StartDate:dd/MM/yyyy HH:mm})" });
                }
                
                if (promotion.EndDate.HasValue && promotion.EndDate.Value < DateTime.Now)
                {
                    return BadRequest(new { success = false, message = $"Promotion {promotion.Name} đã kết thúc vào {promotion.EndDate.Value:dd/MM/yyyy HH:mm}" });
                }

                // ⭐ LUÔN XÓA GIFT ITEMS CŨ TRƯỚC KHI ÁP DỤNG VOUCHER MỚI
                var oldGiftItems = dbCartItems.Where(i => i.IsGift).ToList();
                if (oldGiftItems.Any())
                {
                    _context.CartItems.RemoveRange(oldGiftItems);
                    await _context.SaveChangesAsync();
                }
                cart.CartItems = cart.CartItems.Where(i => !i.IsGift).ToList();

                // Tính tổng tiền đơn hàng (không bao gồm gift items)
                var orderTotal = cart.CartItems.Where(i => !i.IsGift).Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity);

                // Kiểm tra giá trị đơn hàng tối thiểu
                if (userVoucher.PromotionCode?.MinOrderValue.HasValue == true && orderTotal < userVoucher.PromotionCode.MinOrderValue.Value)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Đơn hàng tối thiểu {userVoucher.PromotionCode.MinOrderValue.Value:N0}₫ để sử dụng voucher này"
                    });
                }

                // ⭐ KIỂM TRA SỐ LƯỢNG SẢN PHẨM TỐI THIỂU
                if (promotion?.MinProductQuantity.HasValue == true)
                {
                    int totalProductQty = cart.CartItems.Where(i => !i.IsGift).Sum(i => i.Quantity);
                    if (totalProductQty < promotion.MinProductQuantity.Value)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = $"Đơn hàng cần có tối thiểu {promotion.MinProductQuantity.Value} sản phẩm để sử dụng voucher này"
                        });
                    }
                }

                decimal discountAmount = 0;
                decimal shippingDiscount = 0;
                bool hasGiftItems = false;

                // Áp dụng voucher theo loại
                if (promotion?.Type == PromotionType.Order || promotion?.Type == PromotionType.Product)
                {
                    // Voucher giảm giá đơn hàng/sản phẩm
                    if (userVoucher.PromotionCode?.IsPercent == true && userVoucher.PromotionCode?.Value.HasValue == true)
                    {
                        discountAmount = orderTotal * (userVoucher.PromotionCode.Value.Value / 100);
                        if (userVoucher.PromotionCode.MaxDiscount.HasValue)
                        {
                            discountAmount = Math.Min(discountAmount, userVoucher.PromotionCode.MaxDiscount.Value);
                        }
                    }
                    else if (userVoucher.PromotionCode?.Value.HasValue == true)
                    {
                        discountAmount = userVoucher.PromotionCode.Value.Value;
                    }

                    cart.PromotionCode = userVoucher.PromotionCode?.Code;
                    cart.DiscountAmount = discountAmount;
                }
                else if (promotion?.Type == PromotionType.Shipping)
                {
                    // ⭐ KIỂM TRA MinProductQuantity cho shipping voucher
                    if (promotion.MinProductQuantity.HasValue)
                    {
                        int totalProductQty = cart.CartItems.Where(i => !i.IsGift).Sum(i => i.Quantity);
                        if (totalProductQty < promotion.MinProductQuantity.Value)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message = $"Đơn hàng cần có tối thiểu {promotion.MinProductQuantity.Value} sản phẩm để sử dụng voucher này"
                            });
                        }
                    }

                    // ⭐ KIỂM TRA ApplyDistricts (nếu có WardCode)
                    if (!string.IsNullOrEmpty(promotion.ApplyDistricts) && !string.IsNullOrEmpty(request.WardCode))
                    {
                        var applyAreas = System.Text.Json.JsonSerializer.Deserialize<List<string>>(promotion.ApplyDistricts);
                        
                        if (applyAreas != null && applyAreas.Any())
                        {
                            // Kiểm tra trực tiếp ward code trước
                            if (!applyAreas.Contains(request.WardCode))
                            {
                                // Nếu không khớp, thử convert tên phường sang ward code
                                var wardCodes = new List<string>();
                                
                                foreach (var area in applyAreas)
                                {
                                    // Nếu là số → Ward code
                                    if (area.All(char.IsDigit))
                                    {
                                        wardCodes.Add(area);
                                    }
                                    else
                                    {
                                        // Nếu là text → Tên phường, query để lấy ward code
                                        var shippingFee = await _context.ShippingFees
                                            .FirstOrDefaultAsync(sf => sf.WardName.Contains(area) && sf.IsActive);
                                        
                                        if (shippingFee != null)
                                        {
                                            wardCodes.Add(shippingFee.WardCode);
                                        }
                                    }
                                }

                                // Kiểm tra lại sau khi convert
                                if (!wardCodes.Contains(request.WardCode))
                                {
                                    return BadRequest(new
                                    {
                                        success = false,
                                        message = "Voucher vận chuyển không áp dụng cho khu vực giao hàng này"
                                    });
                                }
                            }
                        }
                    }

                    // Voucher miễn phí ship
                    cart.PromotionCode = userVoucher.PromotionCode?.Code;
                    cart.FreeShipping = true;
                    shippingDiscount = userVoucher.PromotionCode?.Value ?? 0;
                }
                else if (promotion?.Type == PromotionType.Gift)
                {
                    // Voucher tặng quà
                    var giftPromo = promotion.PromotionGifts?.FirstOrDefault();
                    if (giftPromo != null)
                    {
                        // Xóa gift items cũ
                        cart.CartItems = cart.CartItems.Where(i => !i.IsGift).ToList();

                        // Kiểm tra điều kiện mua hàng
                        bool conditionMet = false;
                        string debugInfo = "";
                        
                        if (giftPromo.BuyApplyType == "all")
                        {
                            conditionMet = true;
                            debugInfo = "BuyApplyType = all → OK";
                        }
                        else if (giftPromo.BuyApplyType == "product")
                        {
                            // ⭐ SỬA: Query database trực tiếp như ShoppingCartApiController
                            var buyProductIds = await _context.PromotionGiftBuyProducts
                                .Where(x => x.PromotionGiftId == giftPromo.Id)
                                .Select(x => x.ProductId)
                                .ToListAsync();
                                
                            var cartProductIds = cart.CartItems.Where(i => !i.IsGift).Select(i => i.ProductId).ToList();
                            conditionMet = cart.CartItems.Any(i => buyProductIds.Contains(i.ProductId));
                            
                            // ⭐ THÊM: Check MinQuantity
                            if (conditionMet && giftPromo.BuyConditionType == "MinQuantity")
                            {
                                var requiredQuantity = giftPromo.BuyConditionValue ?? 0;
                                var actualQuantity = cart.CartItems
                                    .Where(i => buyProductIds.Contains(i.ProductId))
                                    .Sum(i => i.Quantity);
                                    
                                conditionMet = actualQuantity >= requiredQuantity;
                                debugInfo += $", Required qty: {requiredQuantity}, Actual qty: {actualQuantity}";
                            }
                            else
                            {
                                debugInfo = $"BuyApplyType = product, Required: [{string.Join(",", buyProductIds)}], Cart: [{string.Join(",", cartProductIds)}]";
                            }
                        }
                        else if (giftPromo.BuyApplyType == "category")
                        {
                            // ⭐ SỬA: Query database trực tiếp như ShoppingCartApiController
                            var buyCategoryIds = await _context.PromotionGiftBuyCategories
                                .Where(x => x.PromotionGiftId == giftPromo.Id)
                                .Select(x => x.CategoryId)
                                .ToListAsync();
                                
                            var cartCategories = cart.CartItems.Where(i => !i.IsGift)
                                .SelectMany(i => i.Product?.ProductCategories?.Select(pc => pc.CategoryId) ?? new List<int>())
                                .Distinct().ToList();
                            conditionMet = cart.CartItems.Any(i => 
                                i.Product?.ProductCategories?.Any(pc => buyCategoryIds.Contains(pc.CategoryId)) == true);
                                
                            // ⭐ THÊM: Check MinQuantity
                            if (conditionMet && giftPromo.BuyConditionType == "MinQuantity")
                            {
                                var requiredQuantity = giftPromo.BuyConditionValue ?? 0;
                                var actualQuantity = cart.CartItems
                                    .Where(i => i.Product?.ProductCategories?.Any(pc => buyCategoryIds.Contains(pc.CategoryId)) == true)
                                    .Sum(i => i.Quantity);
                                    
                                conditionMet = actualQuantity >= requiredQuantity;
                                debugInfo += $", Required qty: {requiredQuantity}, Actual qty: {actualQuantity}";
                            }
                            else
                            {
                                debugInfo = $"BuyApplyType = category, Required: [{string.Join(",", buyCategoryIds)}], Cart: [{string.Join(",", cartCategories)}]";
                            }
                        }
                        else
                        {
                            debugInfo = $"BuyApplyType = {giftPromo.BuyApplyType}, BuyProducts = {giftPromo.BuyProducts?.Count ?? 0}, BuyCategories = {giftPromo.BuyCategories?.Count ?? 0}";
                        }

                        if (!conditionMet)
                        {
                            return BadRequest(new
                            {
                                success = false,
                                message = $"Giỏ hàng không đáp ứng điều kiện để nhận quà. Debug: {debugInfo}"
                            });
                        }

                        // ⭐ SỬA: Thêm gift items - Query database trực tiếp
                        var giftProductIds = await _context.PromotionGiftGiftProducts
                            .Where(x => x.PromotionGiftId == giftPromo.Id)
                            .Select(x => x.ProductId)
                            .ToListAsync();

                        var giftProducts = await _context.Products
                            .Include(p => p.Images)
                            .Where(p => giftProductIds.Contains(p.Id))
                            .ToListAsync();

                        if (giftProducts.Any())
                        {
                            foreach (var product in giftProducts)
                            {
                                    // ⭐ BƯỚC 1: Tìm ProductDiscount cho gift item (nếu có)
                                    decimal? productDiscount = null;
                                    foreach (var discount in activeDiscounts)
                                    {
                                        bool isApplicable = false;
                                        
                                        if (discount.ApplyTo == "all")
                                        {
                                            isApplicable = true;
                                        }
                                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                                        {
                                            var discountProductIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                                            if (discountProductIds != null && discountProductIds.Contains(product.Id))
                                            {
                                                isApplicable = true;
                                            }
                                        }
                                        
                                        if (isApplicable)
                                        {
                                            decimal tempDiscount = 0;
                                            
                                            if (discount.DiscountType == "percent")
                                            {
                                                tempDiscount = product.Price * (discount.DiscountValue / 100);
                                            }
                                            else if (discount.DiscountType == "fixed_amount")
                                            {
                                                tempDiscount = discount.DiscountValue;
                                            }
                                            
                                            if (tempDiscount > (productDiscount ?? 0))
                                            {
                                                productDiscount = tempDiscount;
                                            }
                                        }
                                    }
                                    
                                    // ⭐ BƯỚC 2: Tính giá sau ProductDiscount
                                    decimal priceAfterProductDiscount = product.Price - (productDiscount ?? 0);
                                    
                                    // ⭐ BƯỚC 3: Tính GiftDiscount trên giá sau ProductDiscount
                                    decimal giftDiscount = 0;
                                    if (giftPromo.GiftDiscountType == "free")
                                    {
                                        giftDiscount = priceAfterProductDiscount;
                                    }
                                    else if (giftPromo.GiftDiscountType == "percent" && giftPromo.GiftDiscountValue.HasValue)
                                    {
                                        giftDiscount = priceAfterProductDiscount * (giftPromo.GiftDiscountValue.Value / 100);
                                    }
                                    else if (giftPromo.GiftDiscountType == "money" && giftPromo.GiftDiscountMoneyValue.HasValue)
                                    {
                                        giftDiscount = giftPromo.GiftDiscountMoneyValue.Value;
                                    }

                                    // ⭐ Lưu thêm productDiscount và giftDiscount riêng để frontend dễ hiển thị
                                    var cartItem = new CartItem
                                    {
                                        ProductId = product.Id,
                                        Product = product,
                                        Quantity = giftPromo.GiftQuantity,
                                        IsGift = true,
                                        Discount = (productDiscount ?? 0) + giftDiscount // Tổng discount
                                    };
                                    // Lưu thêm metadata để frontend có thể tách riêng
                                    cartItem.Note = $"{productDiscount ?? 0}|{giftDiscount}"; // Format: "productDiscount|giftDiscount"
                                    cart.CartItems.Add(cartItem);
                            }
                            hasGiftItems = true;
                        }

                        cart.PromotionCode = userVoucher.PromotionCode?.Code;
                    }
                }

                // ⭐ Lưu cart vào DATABASE
                // Lưu cart state
                cartState.PromotionCode = cart.PromotionCode;
                cartState.DiscountAmount = cart.DiscountAmount;
                cartState.FreeShipping = cart.FreeShipping;
                
                // Lưu gift items vào database
                var newGiftItems = cart.CartItems.Where(i => i.IsGift).ToList();
                foreach (var giftItem in newGiftItems)
                {
                    giftItem.UserId = userId;
                    _context.CartItems.Add(giftItem);
                }
                
                await _context.SaveChangesAsync();

                // ⭐ Chuẩn bị thông tin sản phẩm tặng nếu có
                var giftItemsList = new List<object>();
                if (cart.CartItems != null)
                {
                    foreach (var item in cart.CartItems.Where(i => i.IsGift))
                    {
                        // Parse productDiscount và giftDiscount từ Note field
                        decimal productDiscount = 0;
                        decimal giftDiscount = 0;
                        if (!string.IsNullOrEmpty(item.Note))
                        {
                            var parts = item.Note.Split('|');
                            if (parts.Length == 2)
                            {
                                decimal.TryParse(parts[0], out productDiscount);
                                decimal.TryParse(parts[1], out giftDiscount);
                            }
                        }
                        
                        giftItemsList.Add(new
                        {
                            productId = item.ProductId,
                            productName = item.Product?.Name ?? "",
                            productImage = item.Product?.ImageUrl ?? "",
                            quantity = item.Quantity,
                            originalPrice = item.Product?.Price ?? 0,
                            productDiscount = productDiscount,
                            giftDiscount = giftDiscount,
                            discount = item.Discount ?? 0,
                            finalPrice = (item.Product?.Price ?? 0) - (item.Discount ?? 0),
                            isFree = item.Discount >= (item.Product?.Price ?? 0)
                        });
                    }
                }

                return Ok(new
                {
                    success = true,
                    message = "Áp dụng voucher thành công",
                    discountAmount,
                    shippingDiscount,
                    hasGiftItems,
                    giftItems = giftItemsList,
                    voucherCode = userVoucher.PromotionCode?.Code,
                    promotionType = promotion?.Type.ToString() ?? "Unknown"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
    }

    // Request models
    public class CheckoutRequest
    {
        public string PaymentMethod { get; set; } = null!;
        public string? ReceiverName { get; set; }
        public string ShippingAddress { get; set; } = null!;
        public string Phone { get; set; } = null!;
        public string? Note { get; set; }
        public string WardCode { get; set; } = null!;
        public int? SelectedDiscountVoucherId { get; set; }
        public int? SelectedShippingVoucherId { get; set; }
        public int? PointsToUse { get; set; }
    }

    public class ApplyVoucherRequest
    {
        public int? VoucherId { get; set; }
        public string? VoucherType { get; set; } // "discount" hoặc "shipping"
        public string? WardCode { get; set; } // ⭐ THÊM field để validate shipping voucher
    }

    public class CancelOrderRequest
    {
        public string? CancelReason { get; set; }
    }

    public class RequestReturnRequest
    {
        public string Reason { get; set; } = null!;
        public string? ReturnType { get; set; }
        public List<string>? Images { get; set; }
    }

    public class ConfirmPaymentRequest
    {
        public string? OrderId { get; set; }
        public int? Id { get; set; }
        public string PaymentMethod { get; set; } = null!;
        public PaymentDataModel? PaymentData { get; set; }
    }

    public class PaymentDataModel
    {
        public bool Success { get; set; }
        public string ResponseCode { get; set; } = null!;
        public string TxnRef { get; set; } = null!;
        public string TransactionNo { get; set; } = null!;
        public string Amount { get; set; } = null!;
    }
}
