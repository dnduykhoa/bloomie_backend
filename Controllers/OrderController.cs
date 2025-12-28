using Bloomie.Services.Interfaces;
using Bloomie.Services;
using Bloomie.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Extensions;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using QuestPDF.Elements;
using System.IO;
using QRCoder;

namespace Bloomie.Controllers
{
    [Authorize]
    public class OrderController : Controller
    {
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMomoService _momoService;
    private readonly IVNPAYService _vnpayService;
    private readonly IEmailService _emailService;
    private readonly IShippingService _shippingService;
    private readonly INotificationService _notificationService;
    private readonly IHubContext<NotificationHub> _hubContext;

    // Tỷ lệ quy đổi: 100 điểm = 10,000đ
    private const int POINTS_TO_VND = 100; // 100 điểm = 10,000đ (1 điểm = 100đ)

    public OrderController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IMomoService momoService, IVNPAYService vnpayService, IEmailService emailService, IShippingService shippingService, INotificationService notificationService, IHubContext<NotificationHub> hubContext)
    {
        _context = context;
        _userManager = userManager;
        _momoService = momoService;
        _vnpayService = vnpayService;
        _emailService = emailService;
        _shippingService = shippingService;
        _notificationService = notificationService;
        _hubContext = hubContext;
    }

        // Helper method - Lấy cart key từ session
        private string GetCartKey()
        {
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
            string cartKey;

            if (isAuthenticated && User != null)
            {
                cartKey = $"Cart_{_userManager.GetUserId(User)}";
            }
            else
            {
                if (string.IsNullOrEmpty(HttpContext.Session.Id))
                {
                    HttpContext.Session.SetString("TempSessionId", Guid.NewGuid().ToString());
                }
                cartKey = $"Cart_Anonymous_{HttpContext.Session.GetString("TempSessionId") ?? HttpContext.Session.Id}";
            }

            return cartKey;
        }

        // GET: Order/Index - Danh sách đơn hàng của khách hàng
        public async Task<IActionResult> Index(string? status)
        {
            var userId = _userManager.GetUserId(User);
            
            var query = _context.Orders
                .Where(o => o.UserId == userId)
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .AsQueryable();

            // Lọc theo trạng thái nếu có
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            return View(orders);
        }

        // GET: Order/Details/5 - Chi tiết đơn hàng
        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                        .ThenInclude(p => p!.Images)
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Lấy thông tin hoàn trả nếu có
            var orderReturn = await _context.OrderReturns.FirstOrDefaultAsync(r => r.OrderId == id);
            ViewBag.OrderReturn = orderReturn;

            return View(order);
        }

        // GET: Order/Checkout - Trang thanh toán
        public async Task<IActionResult> Checkout(string? voucherCode, int? selectedVoucherId)
        {
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(GetCartKey());
            
            if (cart == null || cart.CartItems == null || !cart.CartItems.Any())
            {
                TempData["error"] = "Giỏ hàng trống. Vui lòng thêm sản phẩm trước khi thanh toán.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // ⭐ CHỈ XÓA gift items nếu KHÔNG CÓ voucherCode trong URL và KHÔNG CÓ PromotionCode trong session
            // (để giữ gift items khi user chuyển từ Cart sang Checkout)
            if (string.IsNullOrEmpty(voucherCode) && string.IsNullOrEmpty(cart.PromotionCode))
            {
                var giftItemsToRemove = cart.CartItems.Where(item => item.IsGift).ToList();
                if (giftItemsToRemove.Any())
                {
                    foreach (var giftItem in giftItemsToRemove)
                    {
                        cart.CartItems.Remove(giftItem);
                    }
                    HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                }
            }

            // KHÔNG load Product từ DB để tránh circular reference
            // Cart trong Session đã có Product info từ khi add to cart
            
            // Tính toán tổng tiền
            // ⭐ Subtotal = Tổng giá ĐÃ GIẢM ProductDiscount (CHƯA trừ Gift discount của voucher Gift)
            // - Sản phẩm thường: Price - Discount (Discount = ProductDiscount)
            // - Sản phẩm tặng: Price (CHƯA trừ Gift discount từ voucher)
            decimal subtotal = cart.CartItems.Sum(item => 
            {
                decimal price = item.Product?.Price ?? 0;
                decimal discount = item.IsGift ? 0 : (item.Discount ?? 0); // Chỉ trừ discount cho non-gift items
                return (price - discount) * item.Quantity;
            });
            
            // ⭐ ItemDiscounts = 0 (không dùng nữa)
            decimal itemDiscounts = 0;
            
            // Giảm giá từ voucher (khách nhập mã)
            decimal promotionDiscount = cart.DiscountAmount ?? 0;
            
            // Phí ship ban đầu là 0, khách sẽ chọn phường/xã để tính
            decimal shippingFee = 0;
            decimal total = subtotal - itemDiscounts - promotionDiscount + shippingFee;

            // Lấy thông tin user hiện tại
            var user = await _userManager.GetUserAsync(User);
            
            // Lấy danh sách vouchers khả dụng của user
            var userId = user?.Id;
            var availableVouchers = new List<UserVoucher>();
            if (!string.IsNullOrEmpty(userId))
            {
                availableVouchers = await _context.UserVouchers
                    .Include(uv => uv.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                            .ThenInclude(p => p!.PromotionGifts)
                    .Where(uv => uv.UserId == userId 
                        && !uv.IsUsed 
                        && uv.ExpiryDate > DateTime.Now)
                    .OrderByDescending(uv => uv.CollectedDate)
                    .ToListAsync();
            }
            
            // Nếu có voucherCode từ URL (từ "Sử dụng ngay"), áp dụng Gift promotion ngay
            if (!string.IsNullOrEmpty(voucherCode) && !string.IsNullOrEmpty(userId))
            {
                var selectedVoucher = availableVouchers.FirstOrDefault(v => v.PromotionCode?.Code == voucherCode);
                if (selectedVoucher != null && selectedVoucher.PromotionCode?.Promotion?.Type == PromotionType.Gift)
                {
                    var promotion = selectedVoucher.PromotionCode.Promotion;
                    var giftEntity = promotion.PromotionGifts?.FirstOrDefault();
                    
                    if (giftEntity != null)
                    {
                        // Lấy danh sách sản phẩm/danh mục cần mua
                        var buyProductIds = await _context.PromotionGiftBuyProducts
                            .Where(x => x.PromotionGiftId == giftEntity.Id)
                            .Select(x => x.ProductId)
                            .ToListAsync();
                        
                        var buyCategoryIds = await _context.PromotionGiftBuyCategories
                            .Where(x => x.PromotionGiftId == giftEntity.Id)
                            .Select(x => x.CategoryId)
                            .ToListAsync();
                        
                        // Kiểm tra sản phẩm trong giỏ có đủ điều kiện không
                        var cartItems = cart.CartItems ?? new List<CartItem>();
                        
                        // ⭐ Load ProductCategories từ database để kiểm tra điều kiện
                        var cartProductIds = cartItems.Where(i => !i.IsGift).Select(i => i.ProductId).ToList();
                        var productsWithCategories = await _context.Products
                            .Include(p => p.ProductCategories)
                            .Where(p => cartProductIds.Contains(p.Id))
                            .ToDictionaryAsync(p => p.Id, p => p);
                        
                        int totalBuyQty = cartItems
                            .Where(i => !i.IsGift && (
                                buyProductIds.Contains(i.ProductId) ||
                                (productsWithCategories.ContainsKey(i.ProductId) && 
                                 productsWithCategories[i.ProductId].ProductCategories != null &&
                                 productsWithCategories[i.ProductId].ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))
                            ))
                            .Sum(i => i.Quantity);
                        
                        decimal totalBuyValue = cartItems
                            .Where(i => !i.IsGift && (
                                buyProductIds.Contains(i.ProductId) ||
                                (productsWithCategories.ContainsKey(i.ProductId) && 
                                 productsWithCategories[i.ProductId].ProductCategories != null &&
                                 productsWithCategories[i.ProductId].ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))
                            ))
                            .Sum(i => (i.Product?.Price ?? 0) * i.Quantity);
                        
                        // Kiểm tra điều kiện
                        bool conditionMet = false;
                        int timesConditionMet = 0;
                        
                        // ⭐ Check nếu không có sản phẩm áp dụng
                        if (totalBuyQty == 0 && totalBuyValue == 0)
                        {
                            TempData["error"] = "Giỏ hàng không có sản phẩm áp dụng cho voucher này.";
                        }
                        else if (giftEntity.BuyConditionType == "MinQuantity" && giftEntity.BuyConditionValue.HasValue)
                        {
                            if (totalBuyQty >= giftEntity.BuyConditionValue.Value)
                            {
                                conditionMet = true;
                                timesConditionMet = totalBuyQty / giftEntity.BuyConditionValue.Value;
                            }
                            else
                            {
                                TempData["error"] = $"Voucher này yêu cầu mua tối thiểu {giftEntity.BuyConditionValue.Value} sản phẩm áp dụng. Bạn chỉ có {totalBuyQty} sản phẩm.";
                            }
                        }
                        else if (giftEntity.BuyConditionType == "MinValue" && giftEntity.BuyConditionValueMoney.HasValue)
                        {
                            if (totalBuyValue >= giftEntity.BuyConditionValueMoney.Value)
                            {
                                conditionMet = true;
                                timesConditionMet = (int)(totalBuyValue / giftEntity.BuyConditionValueMoney.Value);
                            }
                            else
                            {
                                TempData["error"] = $"Voucher này yêu cầu mua tối thiểu {giftEntity.BuyConditionValueMoney.Value:N0}₫ sản phẩm áp dụng. Giá trị đơn hàng hiện tại: {totalBuyValue:N0}₫.";
                            }
                        }
                        
                        if (conditionMet)
                        {
                            // ⭐ Luôn giới hạn tối đa 1 lần mỗi đơn hàng (LimitPerOrder)
                            // Ngay cả khi giá trị đơn hàng cao hơn nhiều lần điều kiện
                            if (timesConditionMet > 1)
                            {
                                timesConditionMet = 1;
                            }
                            
                            int totalGiftQty = giftEntity.GiftQuantity * timesConditionMet;
                            
                            // Lấy danh sách sản phẩm quà tặng
                            var giftProductIds = await _context.PromotionGiftGiftProducts
                                .Where(x => x.PromotionGiftId == giftEntity.Id)
                                .Select(x => x.ProductId)
                                .ToListAsync();
                            
                            var giftCategoryIds = await _context.PromotionGiftGiftCategories
                                .Where(x => x.PromotionGiftId == giftEntity.Id)
                                .Select(x => x.CategoryId)
                                .ToListAsync();
                            
                            var giftProducts = await _context.Products
                                .Include(p => p.Images)
                                .Include(p => p.ProductCategories)
                                .Where(p => giftProductIds.Contains(p.Id) ||
                                    (p.ProductCategories != null && p.ProductCategories.Any(pc => giftCategoryIds.Contains(pc.CategoryId))))
                                .ToListAsync();
                            
                            if (giftProducts.Any())
                            {
                                // Xóa quà tặng cũ
                                cart.CartItems = cart.CartItems?.Where(i => !i.IsGift).ToList() ?? new List<CartItem>();
                                
                                // ⭐ Lấy thông tin ngày giao từ sản phẩm mua đầu tiên (để giao chung với quà tặng)
                                var eligibleBuyItems = cartItems
                                    .Where(i => !i.IsGift && (
                                        buyProductIds.Contains(i.ProductId) ||
                                        (productsWithCategories.ContainsKey(i.ProductId) && 
                                         productsWithCategories[i.ProductId].ProductCategories != null &&
                                         productsWithCategories[i.ProductId].ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))
                                    ))
                                    .ToList();
                                
                                DateTime? commonDeliveryDate = eligibleBuyItems.FirstOrDefault()?.DeliveryDate;
                                string? commonDeliveryTime = eligibleBuyItems.FirstOrDefault()?.DeliveryTime;
                                
                                // Thêm sản phẩm quà tặng
                                int remainingGifts = totalGiftQty;
                                foreach (var giftProduct in giftProducts)
                                {
                                    if (remainingGifts <= 0) break;
                                    
                                    int qtyToAdd = Math.Min(remainingGifts, giftProduct.StockQuantity);
                                    if (qtyToAdd > 0)
                                    {
                                        decimal giftPrice = giftProduct.Price;
                                        decimal giftDiscount = 0;
                                        
                                        // ⭐ Tính ProductDiscount cho sản phẩm quà tặng (nếu có)
                                        decimal productDiscountAmount = 0;
                                        var now = DateTime.Now;
                                        var activeDiscounts = await _context.ProductDiscounts
                                            .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                                            .ToListAsync();
                                        
                                        foreach (var discount in activeDiscounts)
                                        {
                                            bool isApplicable = false;
                                            
                                            if (discount.ApplyTo == "all")
                                            {
                                                isApplicable = true;
                                            }
                                            else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                                            {
                                                var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                                                if (productIds != null && productIds.Contains(giftProduct.Id))
                                                {
                                                    isApplicable = true;
                                                }
                                            }
                                            else if (discount.ApplyTo == "categories" && !string.IsNullOrEmpty(discount.CategoryIds))
                                            {
                                                var categoryIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.CategoryIds);
                                                var productCategoryIds = giftProduct.ProductCategories?.Select(pc => pc.CategoryId).ToList() ?? new List<int>();
                                                if (categoryIds != null && categoryIds.Any(cid => productCategoryIds.Contains(cid)))
                                                {
                                                    isApplicable = true;
                                                }
                                            }
                                            
                                            if (isApplicable)
                                            {
                                                if (discount.DiscountType == "percent")
                                                {
                                                    decimal discountAmount = giftPrice * discount.DiscountValue / 100;
                                                    if (discountAmount > productDiscountAmount)
                                                    {
                                                        productDiscountAmount = discountAmount;
                                                    }
                                                }
                                                else if (discount.DiscountType == "fixed_amount")
                                                {
                                                    if (discount.DiscountValue > productDiscountAmount)
                                                    {
                                                        productDiscountAmount = discount.DiscountValue;
                                                    }
                                                }
                                            }
                                        }
                                        
                                        // ⭐ Giá sau khi trừ ProductDiscount
                                        decimal priceAfterProductDiscount = giftPrice - productDiscountAmount;
                                        
                                        // ⭐ Tính Gift discount từ giá sau khi trừ ProductDiscount
                                        if (giftEntity.GiftDiscountType == "free")
                                        {
                                            giftDiscount = priceAfterProductDiscount; // Miễn phí toàn bộ giá còn lại
                                        }
                                        else if (giftEntity.GiftDiscountType == "percent" && giftEntity.GiftDiscountValue.HasValue)
                                        {
                                            giftDiscount = (priceAfterProductDiscount * giftEntity.GiftDiscountValue.Value) / 100;
                                        }
                                        else if (giftEntity.GiftDiscountType == "money" && giftEntity.GiftDiscountMoneyValue.HasValue)
                                        {
                                            giftDiscount = Math.Min(giftEntity.GiftDiscountMoneyValue.Value, priceAfterProductDiscount);
                                        }
                                        
                                        // ⭐ Tổng discount = ProductDiscount + GiftDiscount
                                        decimal totalDiscount = productDiscountAmount + giftDiscount;
                                        
                        if (cart.CartItems != null)
                        {
                            // ⭐ Tạo Product với Price = giá sau ProductDiscount
                            var simpleProduct = new Product
                            {
                                Id = giftProduct.Id,
                                Name = giftProduct.Name,
                                Price = priceAfterProductDiscount, // ⭐ Giá đã trừ ProductDiscount
                                OriginalPrice = giftPrice, // ⭐ Lưu giá gốc vào OriginalPrice
                                ImageUrl = giftProduct.ImageUrl,
                                StockQuantity = giftProduct.StockQuantity,
                                Images = giftProduct.Images?.Select(img => new ProductImage 
                                { 
                                    Id = img.Id,
                                    Url = img.Url,
                                    ProductId = img.ProductId,
                                    Product = null
                                }).ToList()
                            };
                            
                            cart.CartItems.Add(new CartItem
                            {
                                ProductId = giftProduct.Id,
                                Product = simpleProduct,
                                Quantity = qtyToAdd,
                                IsGift = true,
                                Discount = giftDiscount, // ⭐ Chỉ lưu GiftDiscount (không bao gồm ProductDiscount)
                                DeliveryDate = commonDeliveryDate, // ⭐ Giao chung với sản phẩm mua
                                DeliveryTime = commonDeliveryTime  // ⭐ Cùng khung giờ với sản phẩm mua
                            });
                        }                                        remainingGifts -= qtyToAdd;
                                    }
                                }
                                
                                // ⭐ Xóa DiscountAmount từ voucher cũ (Gift voucher không dùng DiscountAmount)
                                cart.DiscountAmount = 0;
                                cart.PromotionCode = voucherCode; // Lưu mã voucher Gift
                                cart.SelectedVoucherId = selectedVoucher.Id; // ⭐ Lưu ID voucher Gift
                                
                                // Lưu lại cart với sản phẩm quà đã thêm
                                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                                
                                // ⭐ Tính lại subtotal (Price - Discount cho non-gift, Price cho gift)
                                subtotal = cart.CartItems?.Sum(item => 
                                {
                                    decimal price = item.Product?.Price ?? 0;
                                    decimal discount = item.IsGift ? 0 : (item.Discount ?? 0);
                                    return (price - discount) * item.Quantity;
                                }) ?? 0;
                                
                                // ⭐ ItemDiscounts = 0 (không dùng)
                                itemDiscounts = 0;
                                
                                // ⭐ Tính lại Gift Voucher Discount
                                decimal giftVoucherDiscountTemp = cart.CartItems?
                                    .Where(item => item.IsGift)
                                    .Sum(item => (item.Discount ?? 0) * item.Quantity) ?? 0;
                                
                                // ⭐ Gift voucher KHÔNG dùng PromotionDiscount, chỉ dùng GiftVoucherDiscount
                                promotionDiscount = 0;
                                
                                total = subtotal - itemDiscounts - promotionDiscount - giftVoucherDiscountTemp + shippingFee;
                                
                                // ⭐ Thông báo áp dụng voucher thành công
                                TempData["success"] = "Áp dụng voucher thành công!";
                            }
                        }
                    }
                }
            }
            
            // Lấy điểm tích lũy của user
            int availablePoints = 0;
            if (!string.IsNullOrEmpty(userId))
            {
                var userPoints = await _context.UserPoints
                    .FirstOrDefaultAsync(up => up.UserId == userId);
                availablePoints = userPoints?.TotalPoints ?? 0;
            }
            
            // ⭐ Tính Gift Voucher Discount cho hiển thị UI
            // Chỉ áp dụng cho sản phẩm quà tặng (IsGift = true)
            decimal giftVoucherDiscount = cart.CartItems
                .Where(item => item.IsGift)
                .Sum(item => (item.Discount ?? 0) * item.Quantity);
            
            // ⭐ Tính lại total (trừ gift voucher discount)
            total = subtotal - itemDiscounts - promotionDiscount - giftVoucherDiscount + shippingFee;
            
            // ⭐ Tính tổng số lượng sản phẩm
            int totalQuantity = cart.CartItems?.Sum(i => i.Quantity) ?? 0;
            
            ViewBag.Subtotal = subtotal;
            ViewBag.ItemDiscounts = itemDiscounts;
            ViewBag.PromotionDiscount = promotionDiscount;
            ViewBag.GiftVoucherDiscount = giftVoucherDiscount; // ⭐ Discount từ Gift voucher
            ViewBag.ShippingFee = shippingFee;
            ViewBag.Total = total;
            ViewBag.TotalQuantity = totalQuantity; // ⭐ Tổng số lượng
            ViewBag.PromotionCode = cart.PromotionCode;
            // Tạo CartItemsDisplay sạch (không có circular reference)
            var cartItemsDisplay = cart.CartItems.Select(item =>
            {
                var imageUrls = new List<string>();
                if (item.Product?.Images != null)
                {
                    foreach (var img in item.Product.Images)
                    {
                        if (!string.IsNullOrEmpty(img?.Url))
                        {
                            imageUrls.Add(img.Url);
                        }
                    }
                }
                
                return new
                {
                    Product = new
                    {
                        Id = item.Product?.Id ?? 0,
                        Name = item.Product?.Name ?? "",
                        Price = item.Product?.Price ?? 0,
                        OriginalPrice = item.Product?.OriginalPrice, // ⭐ Truyền OriginalPrice
                        ImageUrl = item.Product?.ImageUrl,
                        Images = imageUrls.Select(url => new { Url = url }).ToList()
                    },
                    item.Quantity,
                    item.DeliveryDate,
                    item.DeliveryTime,
                    item.IsGift,
                    item.Discount
                };
            }).ToList();
            
            ViewBag.CartItemsDisplay = cartItemsDisplay;
            ViewBag.UserName = user?.UserName ?? "";
            ViewBag.UserEmail = user?.Email ?? "";
            ViewBag.UserPhone = user?.PhoneNumber ?? "";
            ViewBag.AvailableVouchers = availableVouchers;
            ViewBag.PreselectedVoucherCode = voucherCode; // Truyền mã voucher được chọn từ URL
            
            // Set PreselectedVoucherId để dropdown tự động chọn
            // ⭐ Ưu tiên: voucherCode (URL) > selectedVoucherId (parameter) > cart.SelectedVoucherId (session)
            if (!string.IsNullOrEmpty(voucherCode))
            {
                // Ưu tiên cao nhất: voucherCode từ URL (cho Gift voucher)
                var preselectedVoucher = availableVouchers.FirstOrDefault(v => v.PromotionCode?.Code == voucherCode);
                ViewBag.PreselectedVoucherId = preselectedVoucher?.Id;
            }
            else if (selectedVoucherId.HasValue && selectedVoucherId.Value > 0)
            {
                ViewBag.PreselectedVoucherId = selectedVoucherId.Value;
            }
            else if (cart.SelectedVoucherId.HasValue && cart.SelectedVoucherId.Value > 0)
            {
                // Lấy voucher từ cart nếu user đã chọn ở Shopping Cart
                ViewBag.PreselectedVoucherId = cart.SelectedVoucherId.Value;
            }
            
            ViewBag.AvailablePoints = availablePoints; // Điểm khả dụng
            ViewBag.PointsToVnd = POINTS_TO_VND; // Tỷ lệ quy đổi

            return View();
        }

        // POST: Order/Checkout - Xử lý đặt hàng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(string paymentMethod, string shippingAddress, string phone, string? userName, string? note, string wardCode, int? selectedDiscountVoucherId, int? selectedShippingVoucherId, int? pointsToUse)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(shippingAddress))
            {
                TempData["error"] = "Vui lòng nhập địa chỉ giao hàng.";
                return RedirectToAction("Checkout");
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                TempData["error"] = "Vui lòng nhập số điện thoại.";
                return RedirectToAction("Checkout");
            }

            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                TempData["error"] = "Vui lòng chọn phương thức thanh toán.";
                return RedirectToAction("Checkout");
            }

            if (string.IsNullOrWhiteSpace(wardCode))
            {
                TempData["error"] = "Vui lòng chọn phường/xã giao hàng.";
                return RedirectToAction("Checkout");
            }

            var sessionCart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(GetCartKey());
            if (sessionCart == null || sessionCart.CartItems == null || !sessionCart.CartItems.Any())
            {
                TempData["error"] = "Giỏ hàng trống.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            var userId = _userManager.GetUserId(User);

            // Load đầy đủ thông tin sản phẩm với ProductCategories (dùng AsNoTracking để không ảnh hưởng đến database)
            foreach (var item in sessionCart.CartItems)
            {
                var productFromDb = await _context.Products
                    .AsNoTracking()  // Không tracking để tránh ảnh hưởng đến database
                    .Include(p => p.Images)
                    .Include(p => p.ProductCategories)
                        .ThenInclude(pc => pc.Category)
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId);
                
                if (productFromDb != null)
                {
                    // Tạo bản sao Product đơn giản để lưu vào session (tránh circular reference)
                    item.Product = new Product
                    {
                        Id = productFromDb.Id,
                        Name = productFromDb.Name,
                        Price = productFromDb.Price,
                        ImageUrl = productFromDb.ImageUrl,
                        StockQuantity = productFromDb.StockQuantity,
                        Images = productFromDb.Images?.Select(img => new ProductImage 
                        { 
                            Id = img.Id,
                            Url = img.Url,
                            ProductId = img.ProductId
                        }).ToList(),
                        ProductCategories = productFromDb.ProductCategories?.Select(pc => new ProductCategory
                        {
                            ProductId = pc.ProductId,
                            CategoryId = pc.CategoryId
                        }).ToList()
                    };
                }
            }

            // Kiểm tra tồn kho
            foreach (var item in sessionCart.CartItems)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product == null)
                {
                    TempData["error"] = $"Sản phẩm không tồn tại.";
                    return RedirectToAction("Checkout");
                }
                
                if (!item.IsGift && product.StockQuantity < item.Quantity)
                {
                    TempData["error"] = $"Sản phẩm '{product.Name}' không đủ số lượng trong kho.";
                    return RedirectToAction("Checkout");
                }
            }

            // Tính toán tổng tiền (đã bao gồm giảm giá sản phẩm)
            decimal subtotal = sessionCart.CartItems.Sum(item => 
                ((item.Product?.Price ?? 0) - (item.Discount ?? 0)) * item.Quantity);
            
            decimal promotionDiscount = sessionCart.DiscountAmount ?? 0;
            
            // Xử lý voucher giảm giá từ ví user
            decimal voucherDiscount = 0;
            UserVoucher? selectedDiscountVoucher = null;
            if (selectedDiscountVoucherId.HasValue && selectedDiscountVoucherId.Value > 0)
            {
                selectedDiscountVoucher = await _context.UserVouchers
                    .Include(uv => uv.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                            .ThenInclude(p => p!.PromotionGifts)
                    .FirstOrDefaultAsync(uv => uv.Id == selectedDiscountVoucherId.Value 
                        && uv.UserId == userId 
                        && !uv.IsUsed 
                        && uv.ExpiryDate > DateTime.Now);

                if (selectedDiscountVoucher != null && selectedDiscountVoucher.PromotionCode != null)
                {
                    var promotionCode = selectedDiscountVoucher.PromotionCode;
                    var promotion = promotionCode.Promotion;
                    
                    // Kiểm tra MinOrderValue (điều kiện đơn hàng tối thiểu)
                    if (promotionCode.MinOrderValue.HasValue && subtotal < promotionCode.MinOrderValue.Value)
                    {
                        TempData["error"] = $"Voucher giảm giá này yêu cầu đơn hàng tối thiểu {promotionCode.MinOrderValue.Value:N0}đ";
                        return RedirectToAction("Checkout");
                    }
                    
                    // Kiểm tra ApplyDistricts (điều kiện địa chỉ)
                    if (!string.IsNullOrEmpty(promotion?.ApplyDistricts))
                    {
                        try
                        {
                            var applyDistricts = System.Text.Json.JsonSerializer.Deserialize<List<string>>(promotion.ApplyDistricts);
                            if (applyDistricts != null && applyDistricts.Any())
                            {
                                var isAddressValid = applyDistricts.Any(d => shippingAddress.Contains(d));
                                if (!isAddressValid)
                                {
                                    TempData["error"] = $"Voucher giảm giá không áp dụng cho địa chỉ giao hàng này. Voucher chỉ áp dụng cho: {string.Join(", ", applyDistricts)}";
                                    return RedirectToAction("Checkout", new { selectedVoucherId = selectedDiscountVoucherId });
                                }
                            }
                        }
                        catch { }
                    }
                    
                    // Xử lý theo loại promotion
                    if (promotion?.Type == PromotionType.Gift)
                    {
                        // Xử lý Gift promotion (Mua X tặng Y)
                        var giftEntity = promotion.PromotionGifts?.FirstOrDefault();
                        if (giftEntity != null)
                        {
                            // Lấy danh sách sản phẩm/danh mục cần mua
                            var buyProductIds = await _context.PromotionGiftBuyProducts
                                .Where(x => x.PromotionGiftId == giftEntity.Id)
                                .Select(x => x.ProductId)
                                .ToListAsync();
                            
                            var buyCategoryIds = await _context.PromotionGiftBuyCategories
                                .Where(x => x.PromotionGiftId == giftEntity.Id)
                                .Select(x => x.CategoryId)
                                .ToListAsync();
                            
                            // Tính tổng số lượng sản phẩm đủ điều kiện trong giỏ
                            var cartItems = sessionCart.CartItems ?? new List<CartItem>();
                            int totalBuyQty = cartItems
                                .Where(i => !i.IsGift && (buyProductIds.Contains(i.ProductId) ||
                                    (i.Product != null && i.Product.ProductCategories != null &&
                                        i.Product.ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))))
                                .Sum(i => i.Quantity);
                            
                            // Tính tổng giá trị sản phẩm đủ điều kiện (dùng giá ĐÃ GIẢM ProductDiscount)
                            var eligibleItems = cartItems
                                .Where(i => !i.IsGift && (buyProductIds.Contains(i.ProductId) ||
                                    (i.Product != null && i.Product.ProductCategories != null &&
                                        i.Product.ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))))
                                .ToList();
                            
                            decimal totalBuyValue = eligibleItems
                                .Sum(i => {
                                    decimal price = i.Product?.Price ?? 0;
                                    decimal discount = i.Discount ?? 0;
                                    return (price - discount) * i.Quantity;
                                });
                            
                            // Kiểm tra điều kiện mua
                            bool conditionMet = false;
                            int timesConditionMet = 0;
                            
                            if (giftEntity.BuyConditionType == "MinQuantity" && giftEntity.BuyConditionValue.HasValue)
                            {
                                if (totalBuyQty >= giftEntity.BuyConditionValue.Value)
                                {
                                    conditionMet = true;
                                    timesConditionMet = totalBuyQty / giftEntity.BuyConditionValue.Value;
                                }
                            }
                            else if (giftEntity.BuyConditionType == "MinValue" && giftEntity.BuyConditionValueMoney.HasValue)
                            {
                                if (totalBuyValue >= giftEntity.BuyConditionValueMoney.Value)
                                {
                                    conditionMet = true;
                                    timesConditionMet = (int)(totalBuyValue / giftEntity.BuyConditionValueMoney.Value);
                                }
                            }
                            
                            if (!conditionMet)
                            {
                                TempData["error"] = giftEntity.BuyConditionType == "MinQuantity" 
                                    ? $"Cần mua tối thiểu {giftEntity.BuyConditionValue} sản phẩm để nhận quà tặng"
                                    : $"Cần mua sản phẩm trị giá tối thiểu {giftEntity.BuyConditionValueMoney:N0}₫ để nhận quà tặng";
                                return RedirectToAction("Checkout");
                            }
                            
                            // Áp dụng giới hạn LimitPerOrder
                            if (giftEntity.LimitPerOrder && timesConditionMet > 1)
                            {
                                timesConditionMet = 1;
                            }
                            
                            // Tính tổng số lượng quà tặng
                            int totalGiftQty = giftEntity.GiftQuantity * timesConditionMet;
                            
                            // Lấy danh sách sản phẩm quà tặng
                            var giftProductIds = await _context.PromotionGiftGiftProducts
                                .Where(x => x.PromotionGiftId == giftEntity.Id)
                                .Select(x => x.ProductId)
                                .ToListAsync();
                            
                            var giftCategoryIds = await _context.PromotionGiftGiftCategories
                                .Where(x => x.PromotionGiftId == giftEntity.Id)
                                .Select(x => x.CategoryId)
                                .ToListAsync();
                            
                            var giftProducts = await _context.Products
                                .Include(p => p.Images)
                                .Include(p => p.ProductCategories)
                                .Where(p => giftProductIds.Contains(p.Id) ||
                                    (p.ProductCategories != null && p.ProductCategories.Any(pc => giftCategoryIds.Contains(pc.CategoryId))))
                                .ToListAsync();
                            
                            if (!giftProducts.Any())
                            {
                                TempData["error"] = "Không tìm thấy sản phẩm quà tặng";
                                return RedirectToAction("Checkout");
                            }
                            
                            // Xóa các sản phẩm quà tặng cũ
                            sessionCart.CartItems = sessionCart.CartItems?.Where(i => !i.IsGift).ToList() ?? new List<CartItem>();
                            
                            // ⭐ Lấy ngày giao và khung giờ từ sản phẩm mua đầu tiên (để giao chung với quà tặng)
                            DateTime? commonDeliveryDate = eligibleItems.FirstOrDefault()?.DeliveryDate;
                            string? commonDeliveryTime = eligibleItems.FirstOrDefault()?.DeliveryTime;
                            
                            // Thêm sản phẩm quà tặng vào giỏ
                            int remainingGifts = totalGiftQty;
                            foreach (var giftProduct in giftProducts)
                            {
                                if (remainingGifts <= 0) break;
                                
                                int qtyToAdd = Math.Min(remainingGifts, giftProduct.StockQuantity);
                                if (qtyToAdd > 0)
                                {
                                    // ⭐ Tính giá gốc của sản phẩm quà, sau đó trừ đi ProductDiscount nếu có
                                    decimal giftPrice = giftProduct.Price;
                                    decimal productDiscount = 0;
                                    
                                    // Kiểm tra xem sản phẩm quà có ProductDiscount không
                                    var now = DateTime.Now;
                                    var activeDiscounts = await _context.ProductDiscounts
                                        .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                                        .ToListAsync();
                                    
                                    foreach (var discount in activeDiscounts)
                                    {
                                        bool isApplicable = false;
                                        
                                        if (discount.ApplyTo == "all")
                                        {
                                            isApplicable = true;
                                        }
                                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                                        {
                                            var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                                            if (productIds != null && productIds.Contains(giftProduct.Id))
                                            {
                                                isApplicable = true;
                                            }
                                        }
                                        
                                        if (isApplicable)
                                        {
                                            decimal tempDiscount = 0;
                                            
                                            if (discount.DiscountType == "percent")
                                            {
                                                tempDiscount = giftPrice * (discount.DiscountValue / 100);
                                            }
                                            else if (discount.DiscountType == "fixed_amount")
                                            {
                                                tempDiscount = discount.DiscountValue;
                                            }
                                            
                                            // Chọn discount cao nhất
                                            if (tempDiscount > productDiscount)
                                            {
                                                productDiscount = tempDiscount;
                                            }
                                        }
                                    }
                                    
                                    // Giá thực tế sau khi trừ ProductDiscount
                                    decimal actualPrice = giftPrice - productDiscount;
                                    
                                    // Tính gift discount dựa trên giá thực tế (đã trừ ProductDiscount)
                                    decimal giftDiscount = 0;
                                    
                                    if (giftEntity.GiftDiscountType == "free")
                                    {
                                        giftDiscount = actualPrice; // Miễn phí 100%
                                    }
                                    else if (giftEntity.GiftDiscountType == "percent" && giftEntity.GiftDiscountValue.HasValue)
                                    {
                                        giftDiscount = (actualPrice * giftEntity.GiftDiscountValue.Value) / 100;
                                    }
                                    else if (giftEntity.GiftDiscountType == "money" && giftEntity.GiftDiscountMoneyValue.HasValue)
                                    {
                                        giftDiscount = Math.Min(giftEntity.GiftDiscountMoneyValue.Value, actualPrice);
                                    }
                                    
                                    if (sessionCart.CartItems != null)
                                    {
                                        // Tạo Product object đơn giản để tránh circular reference khi serialize
                                        var simpleGiftProduct = new Product
                                        {
                                            Id = giftProduct.Id,
                                            Name = giftProduct.Name,
                                            Price = actualPrice, // ⭐ Dùng giá đã trừ ProductDiscount
                                            OriginalPrice = giftProduct.Price, // ⭐ Lưu giá gốc
                                            ImageUrl = giftProduct.ImageUrl,
                                            StockQuantity = giftProduct.StockQuantity,
                                            Images = giftProduct.Images?.Select(img => new ProductImage 
                                            { 
                                                Id = img.Id,
                                                Url = img.Url,
                                                ProductId = img.ProductId,
                                                Product = null
                                            }).ToList()
                                        };
                                        
                                        sessionCart.CartItems.Add(new CartItem
                                        {
                                            ProductId = giftProduct.Id,
                                            Product = simpleGiftProduct,
                                            Quantity = qtyToAdd,
                                            IsGift = true,
                                            Discount = giftDiscount,
                                            DeliveryDate = commonDeliveryDate, // ⭐ Giao chung với sản phẩm mua
                                            DeliveryTime = commonDeliveryTime  // ⭐ Cùng khung giờ với sản phẩm mua
                                        });
                                    }
                                    
                                    remainingGifts -= qtyToAdd;
                                }
                            }
                            
                            // Lưu lại session cart
                            HttpContext.Session.SetObjectAsJson(GetCartKey(), sessionCart);
                            
                            // Tính lại subtotal sau khi thêm quà (Price - Discount cho non-gift, Price cho gift)
                            subtotal = sessionCart.CartItems?.Sum(item => 
                            {
                                decimal price = item.Product?.Price ?? 0;
                                decimal discount = item.IsGift ? 0 : (item.Discount ?? 0);
                                return (price - discount) * item.Quantity;
                            }) ?? 0;
                            
                            // Tính voucher discount từ tổng discount của các sản phẩm quà tặng
                            voucherDiscount = sessionCart.CartItems?
                                .Where(i => i.IsGift)
                                .Sum(i => (i.Discount ?? 0) * i.Quantity) ?? 0;
                        }
                    }
                    else
                    {
                        // Xử lý voucher giảm giá (Order/Product/Shipping)
                        if (promotionCode.IsPercent)
                        {
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
                    
                    // ⭐ Kiểm tra quy tắc kết hợp với voucher vận chuyển
                    if (selectedShippingVoucherId.HasValue && selectedShippingVoucherId.Value > 0)
                    {
                        // Chỉ cho phép kết hợp khi AllowCombineShipping = true
                        if (promotion?.AllowCombineShipping != true)
                        {
                            TempData["error"] = "Voucher giảm giá này không cho phép kết hợp với voucher vận chuyển.";
                            return RedirectToAction("Checkout");
                        }
                    }
                }
            }
            
            // Xử lý voucher vận chuyển từ ví user
            decimal shippingVoucherDiscount = 0;
            UserVoucher? selectedShippingVoucher = null;
            if (selectedShippingVoucherId.HasValue && selectedShippingVoucherId.Value > 0)
            {
                selectedShippingVoucher = await _context.UserVouchers
                    .Include(uv => uv.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                    .FirstOrDefaultAsync(uv => uv.Id == selectedShippingVoucherId.Value 
                        && uv.UserId == userId 
                        && !uv.IsUsed 
                        && uv.ExpiryDate > DateTime.Now);

                if (selectedShippingVoucher != null && selectedShippingVoucher.PromotionCode != null)
                {
                    var promotionCode = selectedShippingVoucher.PromotionCode;
                    var promotion = promotionCode.Promotion;
                    
                    // Kiểm tra MinOrderValue (điều kiện đơn hàng tối thiểu)
                    if (promotionCode.MinOrderValue.HasValue && subtotal < promotionCode.MinOrderValue.Value)
                    {
                        TempData["error"] = $"Voucher vận chuyển này yêu cầu đơn hàng tối thiểu {promotionCode.MinOrderValue.Value:N0}đ";
                        return RedirectToAction("Checkout");
                    }
                    
                    // Kiểm tra ApplyDistricts (điều kiện địa chỉ)
                    if (!string.IsNullOrEmpty(promotion?.ApplyDistricts))
                    {
                        try
                        {
                            var applyDistricts = System.Text.Json.JsonSerializer.Deserialize<List<string>>(promotion.ApplyDistricts);
                            if (applyDistricts != null && applyDistricts.Any())
                            {
                                var isAddressValid = applyDistricts.Any(d => shippingAddress.Contains(d));
                                if (!isAddressValid)
                                {
                                    TempData["error"] = $"Voucher vận chuyển không áp dụng cho địa chỉ giao hàng này. Voucher chỉ áp dụng cho: {string.Join(", ", applyDistricts)}";
                                    return RedirectToAction("Checkout", new { selectedVoucherId = selectedShippingVoucherId });
                                }
                            }
                        }
                        catch { }
                    }
                    
                    // Tính phí ship trước để tính discount
                    decimal? tempShippingFee = await _shippingService.GetShippingFee(wardCode);
                    if (tempShippingFee == null)
                    {
                        TempData["error"] = "Phường/xã bạn chọn chưa hỗ trợ giao hàng. Vui lòng chọn phường/xã khác.";
                        return RedirectToAction("Checkout");
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
                    
                    // Áp dụng max discount nếu có
                    if (promotionCode.MaxDiscount.HasValue && shippingVoucherDiscount > promotionCode.MaxDiscount.Value)
                    {
                        shippingVoucherDiscount = promotionCode.MaxDiscount.Value;
                    }
                    
                    // Đảm bảo discount không vượt quá phí ship
                    if (shippingVoucherDiscount > baseFee)
                    {
                        shippingVoucherDiscount = baseFee;
                    }
                    
                    // ⭐ Kiểm tra quy tắc kết hợp với voucher giảm giá
                    if (selectedDiscountVoucherId.HasValue && selectedDiscountVoucherId.Value > 0 && selectedDiscountVoucher != null)
                    {
                        var discountPromotionType = selectedDiscountVoucher.PromotionCode?.Promotion?.Type;
                        
                        // Kiểm tra AllowCombineOrder nếu voucher giảm giá là Order discount
                        // Chỉ cho phép kết hợp khi AllowCombineOrder = true
                        if (discountPromotionType == PromotionType.Order && promotion?.AllowCombineOrder != true)
                        {
                            TempData["error"] = "Voucher vận chuyển này không cho phép kết hợp với voucher giảm giá đơn hàng.";
                            return RedirectToAction("Checkout");
                        }
                        
                        // Kiểm tra AllowCombineProduct nếu voucher giảm giá là Product discount
                        // Chỉ cho phép kết hợp khi AllowCombineProduct = true
                        if (discountPromotionType == PromotionType.Product && promotion?.AllowCombineProduct != true)
                        {
                            TempData["error"] = "Voucher vận chuyển này không cho phép kết hợp với voucher giảm giá sản phẩm.";
                            return RedirectToAction("Checkout");
                        }
                        
                        // Kiểm tra AllowCombineOrder cho Gift voucher (Gift cũng là loại giảm giá đơn hàng)
                        // Chỉ cho phép kết hợp khi AllowCombineOrder = true
                        if (discountPromotionType == PromotionType.Gift && promotion?.AllowCombineOrder != true)
                        {
                            TempData["error"] = "Voucher vận chuyển này không cho phép kết hợp với voucher quà tặng.";
                            return RedirectToAction("Checkout");
                        }
                    }
                }
            }
            
            // ⭐ Tính tổng discount: Nếu có selectedDiscountVoucherId thì chỉ dùng voucherDiscount (đã tính lại)
            // Nếu không có thì dùng promotionDiscount từ session (voucher từ cart)
            decimal totalDiscount = selectedDiscountVoucherId.HasValue && selectedDiscountVoucherId.Value > 0 
                ? voucherDiscount 
                : promotionDiscount;
            
            // Xử lý điểm tích lũy
            decimal pointsDiscount = 0;
            int actualPointsUsed = 0;
            if (pointsToUse.HasValue && pointsToUse.Value > 0)
            {
                // Kiểm tra số điểm khả dụng
                var userPoints = await _context.UserPoints.FirstOrDefaultAsync(up => up.UserId == userId);
                if (userPoints == null || userPoints.TotalPoints < pointsToUse.Value)
                {
                    TempData["error"] = "Bạn không có đủ điểm để sử dụng.";
                    return RedirectToAction("Checkout");
                }
                
                // Tính số tiền giảm từ điểm (1 điểm = 100đ)
                pointsDiscount = pointsToUse.Value * POINTS_TO_VND;
                
                // Đảm bảo không giảm quá tổng tiền (subtotal - các discount khác + ship)
                decimal maxDiscountAllowed = subtotal - totalDiscount + (sessionCart.FreeShipping ? 0 : (await _shippingService.GetShippingFee(wardCode) ?? 0));
                if (pointsDiscount > maxDiscountAllowed)
                {
                    pointsDiscount = maxDiscountAllowed;
                    actualPointsUsed = (int)(pointsDiscount / POINTS_TO_VND);
                }
                else
                {
                    actualPointsUsed = pointsToUse.Value;
                }
            }
            
            // Tính phí ship dựa trên wardCode
            decimal? shippingFeeNullable = await _shippingService.GetShippingFee(wardCode);
            if (shippingFeeNullable == null)
            {
                TempData["error"] = "Phường/xã bạn chọn chưa hỗ trợ giao hàng. Vui lòng chọn phường/xã khác.";
                return RedirectToAction("Checkout");
            }
            decimal shippingFeeOriginal = sessionCart.FreeShipping ? 0 : shippingFeeNullable.Value;
            
            // Tính phí ship cuối cùng sau khi áp dụng shipping voucher discount
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
                PaymentMethod = paymentMethod,
                PaymentStatus = paymentMethod == "COD" ? "Chưa thanh toán" : "Chờ thanh toán",
                ReceiverName = userName,
                ShippingAddress = shippingAddress,
                Phone = phone,
                PointsUsed = actualPointsUsed,
                // ⭐ Lưu thông tin giảm giá: Chỉ lưu 1 trong 2 (voucher mới hoặc voucher từ cart)
                PromotionDiscount = selectedDiscountVoucherId.HasValue && selectedDiscountVoucherId.Value > 0 ? 0 : promotionDiscount,
                VoucherDiscount = selectedDiscountVoucherId.HasValue && selectedDiscountVoucherId.Value > 0 ? voucherDiscount : 0,
                ShippingDiscount = shippingVoucherDiscount,
                PointsDiscount = pointsDiscount,
                ShippingFee = shippingFeeOriginal,  // Lưu giá gốc, không trừ discount
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

                    // ⭐ Tính giá đơn vị (UnitPrice)
                    decimal unitPrice;
                    
                    if (item.IsGift)
                    {
                        // Với sản phẩm quà tặng, dùng Price từ cart (đã trừ ProductDiscount)
                        // KHÔNG trừ thêm Discount (gift voucher discount) vì đã tính riêng trong VoucherDiscount
                        unitPrice = item.Product?.Price ?? product.Price;
                    }
                    else
                    {
                        // Với sản phẩm thường, dùng Price từ cart và trừ discount nếu có
                        unitPrice = item.Product?.Price ?? product.Price;
                        if (item.Discount.HasValue && item.Discount.Value > 0)
                        {
                            unitPrice -= item.Discount.Value;
                        }
                    }

                    var orderDetail = new OrderDetail
                    {
                        ProductId = item.ProductId,
                        // Không cần FlowerVariantId
                        Quantity = item.Quantity,
                        UnitPrice = unitPrice,
                        DeliveryDate = item.DeliveryDate,
                        DeliveryTime = item.DeliveryTime,
                        Note = item.Note,
                        IsGift = item.IsGift  // Đánh dấu sản phẩm là quà tặng
                    };

                    order.OrderDetails.Add(orderDetail);

                    // ❌ KHÔNG trừ số lượng khi đặt hàng - Chỉ trừ khi Staff xác nhận
                    // Số lượng sẽ được trừ trong Admin area khi staff xác nhận đơn hàng
                }
            }

            // Lưu đơn hàng
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Sinh OrderId ngẫu nhiên dạng yyMMdd + Id + 3 ký tự chữ
            string randomStr = "";
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var rand = new Random();
            for (int i = 0; i < 3; i++) randomStr += chars[rand.Next(chars.Length)];
            string paddedId = order.Id.ToString().PadLeft(2, '0');
            order.OrderId = $"{DateTime.Now:yyMMdd}{paddedId}{randomStr}";
            
            // Nếu đơn hàng thanh toán online (Momo/VNPay), đặt lịch tự động hủy sau 30 phút
            if (paymentMethod == "Momo" || paymentMethod == "VNPAY")
            {
                var jobId = Bloomie.Services.Implementations.OrderCancellationService.ScheduleCancellation(order.Id);
                order.CancellationJobId = jobId;
            }
            
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            // 🔔 GỬI THÔNG BÁO REALTIME CHO ADMIN
            try
            {
                var customerName = (await _userManager.GetUserAsync(User))?.FullName ?? "Khách hàng";
                var customerUsername = (await _userManager.GetUserAsync(User))?.UserName ?? "N/A";
                
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

            // Mark vouchers as used nếu có chọn voucher
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

            // Trừ điểm nếu có sử dụng (CHỈ với COD - thanh toán online sẽ trừ sau khi callback thành công)
            if (actualPointsUsed > 0 && paymentMethod == "COD")
            {
                var userPoints = await _context.UserPoints.FirstOrDefaultAsync(up => up.UserId == userId);
                if (userPoints != null)
                {
                    userPoints.TotalPoints -= actualPointsUsed;
                    userPoints.LastUpdated = DateTime.Now;
                    _context.UserPoints.Update(userPoints);
                    
                    // Ghi lại lịch sử sử dụng điểm
                    var pointHistory = new PointHistory
                    {
                        UserId = userId!,
                        Points = -actualPointsUsed, // Âm vì trừ điểm
                        Reason = $"Sử dụng điểm cho đơn hàng {order.OrderId}",
                        CreatedDate = DateTime.Now,
                        OrderId = order.Id
                    };
                    _context.PointHistories.Add(pointHistory);
                    await _context.SaveChangesAsync();
                }
            }

            // Cập nhật số lượt sử dụng mã giảm giá nếu có (mã giảm giá cũ - không phải từ voucher wallet)
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

            // Xóa giỏ hàng
            HttpContext.Session.Remove(GetCartKey());

            TempData["success"] = "Đặt hàng thành công!";
            
            // Chuyển đến trang thanh toán tùy theo phương thức
            if (paymentMethod == "Momo")
            {
                // Không thay đổi Status, giữ nguyên "Chờ xác nhận"
                // PaymentStatus đã được set thành "Chờ thanh toán" ở trên
                _context.Orders.Update(order);
                await _context.SaveChangesAsync();

                // Lấy thông tin user để gửi sang Momo
                var user = await _userManager.GetUserAsync(User);

                var momoModel = new Bloomie.Models.Momo.OrderInfoModel
                {
                    OrderId = order.OrderId, // Sử dụng OrderId của hệ thống
                    FullName = user?.UserName ?? "Khách hàng",
                    Amount = (double)totalAmount,
                    OrderInformation = $"Thanh toán đơn hàng {order.OrderId}"
                };
                
                try
                {
                    var momoResponse = await _momoService.CreatePaymentMomo(momoModel);
                    if (momoResponse != null && !string.IsNullOrEmpty(momoResponse.PayUrl))
                    {
                        return Redirect(momoResponse.PayUrl);
                    }
                    else
                    {
                        TempData["error"] = $"Không tạo được yêu cầu thanh toán Momo. Lỗi: {momoResponse?.Message ?? "Không có phản hồi từ Momo"}";
                        return RedirectToAction("OrderSuccess", new { orderId = order.Id });
                    }
                }
                catch (Exception ex)
                {
                    TempData["error"] = $"Lỗi kết nối Momo: {ex.Message}";
                    return RedirectToAction("OrderSuccess", new { orderId = order.Id });
                }
            }
            else if (paymentMethod == "VNPAY")
            {
                // Tạo model cho VNPAY
                var vnpayModel = new Bloomie.Models.Vnpay.PaymentInformationModel
                {
                    OrderType = "billpayment", // hoặc loại phù hợp
                    Amount = order.TotalAmount,
                    OrderDescription = $"Thanh toán đơn hàng {order.OrderId}",
                    Name = order.ShippingAddress ?? "Khách hàng",
                    TxnRef = order.OrderId // Mã đơn hàng duy nhất
                };
                var paymentUrl = _vnpayService.CreatePaymentUrl(vnpayModel, HttpContext);
                if (!string.IsNullOrEmpty(paymentUrl))
                {
                    return Redirect(paymentUrl);
                }
                else
                {
                    TempData["error"] = "Không tạo được yêu cầu thanh toán VNPAY.";
                    return RedirectToAction("OrderSuccess", new { orderId = order.Id });
                }
            }
            else // COD
            {
                return RedirectToAction("OrderSuccess", new { orderId = order.Id });
            }
        }

        // GET: Order/OrderSuccess/5 - Trang thông báo đặt hàng thành công
        public async Task<IActionResult> OrderSuccess(int orderId)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                        .ThenInclude(p => p!.Images)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            return View(order);
        }

        // POST: Order/CancelOrder/5 - Hủy đơn hàng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelOrder(int id, string? cancelReason)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Chỉ cho phép hủy đơn hàng ở trạng thái "Chờ xác nhận"
            if (order.Status != "Chờ xác nhận" && order.Status != "Chờ thanh toán" && order.PaymentStatus != "Đã thanh toán")
            {
                TempData["error"] = "Không thể hủy đơn hàng ở trạng thái hiện tại. Vui lòng liên hệ với chúng tôi.";
                return RedirectToAction("Details", new { id });
            }

            // ❌ KHÔNG hoàn lại số lượng vì chưa trừ kho khi đặt hàng
            // Kho chỉ được trừ khi Staff xác nhận đơn hàng
            // Nếu đơn hàng chưa được xác nhận thì không cần hoàn lại

            // Lưu lý do hủy và thời gian hủy
            order.Status = "Đã hủy";
            order.CancelReason = cancelReason;
            order.CancelledAt = DateTime.Now;
            
            await _context.SaveChangesAsync();

            // Gửi email xác nhận hủy đơn hàng cho khách hàng
            await SendOrderCancelledByCustomerEmailAsync(order, cancelReason);

            TempData["success"] = "Đã hủy đơn hàng thành công.";
            return RedirectToAction("Details", new { id });
        }

        // POST: Order/ConfirmReceived/5 - Xác nhận đã nhận hàng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmReceived(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Chỉ cho phép xác nhận khi đơn hàng đã giao
            if (order.Status != "Đã giao")
            {
                TempData["error"] = "Không thể xác nhận ở trạng thái hiện tại.";
                return RedirectToAction("Details", new { id });
            }

            order.Status = "Hoàn thành";
            await _context.SaveChangesAsync();

            TempData["success"] = "Đã xác nhận nhận hàng thành công. Cảm ơn bạn đã mua hàng!";
            return RedirectToAction("Details", new { id });
        }

        // GET: Order/TrackOrder/5 - Theo dõi đơn hàng
        public async Task<IActionResult> TrackOrder(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                        .ThenInclude(p => p!.Images)
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Lấy thông tin hoàn trả nếu có
            var orderReturn = await _context.OrderReturns.FirstOrDefaultAsync(r => r.OrderId == id);
            ViewBag.OrderReturn = orderReturn;

            return View(order);
        }

        // GET: Order/Reorder/5 - Đặt lại đơn hàng
        public async Task<IActionResult> Reorder(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Tạo giỏ hàng mới từ đơn hàng cũ
            var cart = new ShoppingCart
            {
                CartItems = new List<CartItem>()
            };

            foreach (var detail in order.OrderDetails ?? new List<OrderDetail>())
            {
                if (detail.Product != null)
                {
                    // Kiểm tra còn hàng không
                    if (detail.Product.StockQuantity < detail.Quantity)
                    {
                        TempData["warning"] = $"Sản phẩm '{detail.Product.Name}' không đủ số lượng trong kho.";
                        continue;
                    }

                    cart.CartItems.Add(new CartItem
                    {
                        ProductId = detail.ProductId,
                        Quantity = detail.Quantity,
                        Product = detail.Product,
                        DeliveryDate = DateTime.Now.AddDays(1).Date,
                        DeliveryTime = "08:00 - 10:00"
                    });
                }
            }

            // Lưu giỏ hàng vào session
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);

            TempData["success"] = "Đã thêm sản phẩm từ đơn hàng cũ vào giỏ hàng.";
            return RedirectToAction("Index", "ShoppingCart");
        }

        // GET: Order/RetryPayment/5 - Thanh toán lại đơn hàng
        public async Task<IActionResult> RetryPayment(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Chỉ cho phép thanh toán lại nếu PaymentStatus là "Chờ thanh toán"
            if (order.PaymentStatus != "Chờ thanh toán")
            {
                TempData["error"] = "Đơn hàng này không thể thanh toán lại.";
                return RedirectToAction("Details", new { id });
            }

            // Gọi lại service Momo để tạo link thanh toán mới
            if (order.PaymentMethod == "Momo")
            {
                var user = await _userManager.GetUserAsync(User);
                
                // Tạo orderId mới cho Momo (thêm timestamp để tránh trùng)
                var newMomoOrderId = $"{order.OrderId}_{DateTime.UtcNow.Ticks}";
                
                var momoModel = new Bloomie.Models.Momo.OrderInfoModel
                {
                    OrderId = newMomoOrderId, // Sử dụng orderId mới
                    FullName = user?.UserName ?? "Khách hàng",
                    Amount = (double)order.TotalAmount,
                    OrderInformation = $"Thanh toán lại đơn hàng {order.OrderId}"
                };

                try
                {
                    var momoResponse = await _momoService.CreatePaymentMomo(momoModel);
                    if (momoResponse != null && !string.IsNullOrEmpty(momoResponse.PayUrl))
                    {
                        return Redirect(momoResponse.PayUrl);
                    }
                    else
                    {
                        TempData["error"] = $"Không tạo được yêu cầu thanh toán Momo. Lỗi: {momoResponse?.Message ?? "Không có phản hồi từ Momo"}";
                        return RedirectToAction("Details", new { id });
                    }
                }
                catch (Exception ex)
                {
                    TempData["error"] = $"Lỗi kết nối Momo: {ex.Message}";
                    return RedirectToAction("Details", new { id });
                }
            }
            else if (order.PaymentMethod == "VNPAY")
            {
                // Tạo model cho VNPAY
                var vnpayModel = new Bloomie.Models.Vnpay.PaymentInformationModel
                {
                    OrderType = "billpayment",
                    Amount = order.TotalAmount,
                    OrderDescription = $"Thanh toán lại đơn hàng {order.OrderId}",
                    Name = order.ShippingAddress ?? "Khách hàng",
                    TxnRef = order.OrderId // Sử dụng OrderId gốc
                };
                
                var paymentUrl = _vnpayService.CreatePaymentUrl(vnpayModel, HttpContext);
                if (!string.IsNullOrEmpty(paymentUrl))
                {
                    return Redirect(paymentUrl);
                }
                else
                {
                    TempData["error"] = "Không tạo được yêu cầu thanh toán VNPAY.";
                    return RedirectToAction("Details", new { id });
                }
            }
            
            TempData["error"] = "Phương thức thanh toán không được hỗ trợ.";
            return RedirectToAction("Details", new { id });
        }

        // GET: Order/ExportInvoicePdf/5 - Xuất hóa đơn PDF
        [HttpGet]
        public async Task<IActionResult> ExportInvoicePdf(int orderId)
        {
            var userId = _userManager.GetUserId(User);
            var user = await _userManager.GetUserAsync(User);
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (order == null)
            {
                return NotFound();
            }

            // Tính toán chi tiết giá
            decimal subtotal = order.OrderDetails?.Sum(d => d.UnitPrice * d.Quantity) ?? 0;
            decimal shippingFee = 30000; // Mặc định phí ship
            decimal discount = subtotal + shippingFee - order.TotalAmount;

            // Tạo PDF chuyên nghiệp bằng QuestPDF
            var stream = new MemoryStream();
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    // Header - Logo và thông tin shop
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("BLOOMIE FLOWER SHOP").FontSize(20).Bold().FontColor("#D0021B");
                            col.Item().Text("Địa chỉ: 123 Đường ABC, Quận 1, TP.HCM").FontSize(9);
                            col.Item().Text("Điện thoại: 0123-456-789 | Email: contact@bloomie.vn").FontSize(9);
                            col.Item().Text("Website: www.bloomie.vn").FontSize(9);
                        });
                        
                        row.ConstantItem(120).AlignRight().Column(col =>
                        {
                            col.Item().Border(1).BorderColor("#D0021B").Padding(5).Column(c =>
                            {
                                c.Item().AlignCenter().Text("HÓA ĐƠN").Bold().FontSize(14).FontColor("#D0021B");
                                c.Item().AlignCenter().Text($"Mã đơn: {order.OrderId}").FontSize(12).Bold();
                            });
                        });
                    });

                    // Content
                    page.Content().PaddingTop(20).Column(col =>
                    {
                        // Thông tin khách hàng và đơn hàng
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("THÔNG TIN KHÁCH HÀNG").Bold().FontSize(12).FontColor("#333");
                                c.Item().PaddingTop(5).Text($"Họ tên: {user?.UserName ?? ""}");
                                c.Item().Text($"Email: {user?.Email ?? ""}");
                                c.Item().Text($"Số điện thoại: {order.Phone}");
                                c.Item().Text($"Địa chỉ: {order.ShippingAddress}");
                            });

                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("THÔNG TIN ĐƠN HÀNG").Bold().FontSize(12).FontColor("#333");
                                c.Item().PaddingTop(5).Text($"Ngày đặt: {order.OrderDate:dd/MM/yyyy HH:mm}");
                                c.Item().Text($"Trạng thái: {order.Status}");
                                c.Item().Text($"Phương thức thanh toán: {order.PaymentMethod}");
                                
                                // Hiển thị trạng thái thanh toán với màu sắc
                                if (order.PaymentMethod == "Momo" || order.PaymentMethod == "VNPAY")
                                {
                                    if (order.PaymentStatus == "Đã thanh toán")
                                    {
                                        c.Item().Text($"Tình trạng: {order.PaymentStatus}").FontColor("#28a745").Bold();
                                    }
                                    else
                                    {
                                        c.Item().Text($"Tình trạng: {order.PaymentStatus}").FontColor("#ffc107");
                                    }
                                }
                                else if (order.PaymentMethod == "COD")
                                {
                                    c.Item().Text("Tình trạng: Thanh toán khi nhận hàng").FontColor("#666");
                                }
                            });
                        });

                        // Đường kẻ phân cách
                        col.Item().PaddingTop(15).PaddingBottom(10).LineHorizontal(1).LineColor("#DDD");

                        // Bảng sản phẩm
                        col.Item().Text("CHI TIẾT ĐƠN HÀNG").Bold().FontSize(12).FontColor("#333");
                        col.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(40);      // STT
                                columns.RelativeColumn(4);       // Tên sản phẩm
                                columns.RelativeColumn(1.5f);    // Số lượng
                                columns.RelativeColumn(2);       // Đơn giá
                                columns.RelativeColumn(2);       // Thành tiền
                            });

                            // Header
                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).AlignCenter().Text("STT").Bold();
                                header.Cell().Element(CellStyle).Text("Sản phẩm").Bold();
                                header.Cell().Element(CellStyle).AlignCenter().Text("Số lượng").Bold();
                                header.Cell().Element(CellStyle).AlignRight().Text("Đơn giá").Bold();
                                header.Cell().Element(CellStyle).AlignRight().Text("Thành tiền").Bold();

                                static IContainer CellStyle(IContainer c)
                                {
                                    return c.Background("#F5F5F5").Border(1).BorderColor("#DDD").Padding(8);
                                }
                            });

                            // Dữ liệu
                            int stt = 1;
                            foreach (var detail in order.OrderDetails ?? new List<OrderDetail>())
                            {
                                table.Cell().Element(CellStyle).AlignCenter().Text(stt++);
                                table.Cell().Element(CellStyle).Column(c =>
                                {
                                    c.Item().Text(detail.Product?.Name ?? "");
                                    if (detail.DeliveryDate.HasValue)
                                    {
                                        c.Item().Text($"Giao: {detail.DeliveryDate:dd/MM/yyyy} {detail.DeliveryTime}").FontSize(9).Italic().FontColor("#666");
                                    }
                                });
                                table.Cell().Element(CellStyle).AlignCenter().Text(detail.Quantity);
                                table.Cell().Element(CellStyle).AlignRight().Text($"{detail.UnitPrice:#,##0} đ");
                                table.Cell().Element(CellStyle).AlignRight().Text($"{detail.UnitPrice * detail.Quantity:#,##0} đ");

                                static IContainer CellStyle(IContainer c)
                                {
                                    return c.Border(1).BorderColor("#DDD").Padding(8);
                                }
                            }
                        });

                        // Tổng tiền
                        col.Item().PaddingTop(10).AlignRight().Column(c =>
                        {
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(150).Text("Tạm tính:").FontSize(11);
                                r.ConstantItem(120).AlignRight().Text($"{subtotal:#,##0} đ").FontSize(11);
                            });

                            if (discount > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(150).Text("Giảm giá:").FontSize(11).FontColor("#28a745");
                                    r.ConstantItem(120).AlignRight().Text($"-{discount:#,##0} đ").FontSize(11).FontColor("#28a745");
                                });
                            }

                            c.Item().Row(r =>
                            {
                                r.ConstantItem(150).Text("Phí vận chuyển:").FontSize(11);
                                r.ConstantItem(120).AlignRight().Text($"{shippingFee:#,##0} đ").FontSize(11);
                            });

                            c.Item().PaddingTop(5).LineHorizontal(1).LineColor("#333");

                            c.Item().PaddingTop(5).Row(r =>
                            {
                                r.ConstantItem(150).Text("TỔNG CỘNG:").Bold().FontSize(14).FontColor("#D0021B");
                                r.ConstantItem(120).AlignRight().Text($"{order.TotalAmount:#,##0} đ").Bold().FontSize(14).FontColor("#D0021B");
                            });
                        });

                        // Ghi chú
                        col.Item().PaddingTop(20).Column(c =>
                        {
                            c.Item().Text("GHI CHÚ:").Bold().FontSize(10);
                            c.Item().Text("- Vui lòng kiểm tra kỹ sản phẩm khi nhận hàng.").FontSize(9);
                            c.Item().Text("- Mọi thắc mắc vui lòng liên hệ hotline: 0123-456-789").FontSize(9);
                            c.Item().Text("- Hóa đơn này là bằng chứng giao dịch hợp lệ.").FontSize(9);
                        });

                        // QR Code ở giữa khoảng trắng
                        col.Item().PaddingTop(60).AlignCenter().Column(c =>
                        {
                            c.Item().AlignCenter().Text("Kết nối với chúng tôi").Bold().FontSize(10).FontColor("#D0021B");
                            
                            // Generate QR Code cho website/social media
                            using (var qrGenerator = new QRCodeGenerator())
                            {
                                var qrCodeData = qrGenerator.CreateQrCode("https://www.facebook.com/profile.php?id=61583542235441", QRCodeGenerator.ECCLevel.Q);
                                using (var qrCode = new PngByteQRCode(qrCodeData))
                                {
                                    var qrCodeImage = qrCode.GetGraphic(20);
                                    c.Item().PaddingTop(10).AlignCenter().Width(120).Height(120).Image(qrCodeImage);
                                }
                            }
                            
                            c.Item().PaddingTop(5).AlignCenter().Text("Quét mã để theo dõi Facebook").FontSize(9).FontColor("#666");
                        });
                    });

                    // Footer
                    page.Footer().AlignCenter().Column(col =>
                    {
                        col.Item().LineHorizontal(1).LineColor("#DDD");
                        col.Item().PaddingTop(10).Text("Cảm ơn quý khách đã tin tưởng và lựa chọn Bloomie Flower Shop!").FontSize(10).Italic().FontColor("#666");
                        col.Item().Text("Chúc quý khách luôn vui vẻ và hạnh phúc! 🌸").FontSize(9).FontColor("#D0021B");
                    });
                });
            }).GeneratePdf(stream);
            
            stream.Position = 0;
            return File(stream, "application/pdf", $"HoaDon_Bloomie_{order.Id}_{DateTime.Now:yyyyMMdd}.pdf");
        }

        // Hàm gửi email xác nhận khi khách hàng tự hủy đơn hàng
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
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
                        .container {{ max-width: 600px; margin: 30px auto; background-color: #fff; border-radius: 10px; box-shadow: 0 4px 12px rgba(0,0,0,0.08); overflow: hidden; }}
                        .header {{ background-color: #6c757d; padding: 24px; text-align: center; }}
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
                            <h2>Xác nhận hủy đơn hàng</h2>
                            <div class='order-info'>
                                <strong>Mã đơn hàng:</strong> #{order.OrderId}<br/>
                                <strong>Thời gian hủy:</strong> {DateTime.Now:HH:mm dd/MM/yyyy}<br/>
                                {reasonText}
                                <strong>Tổng tiền:</strong> {order.TotalAmount:N0} VNĐ<br/>
                            </div>
                            <p>Chúng tôi đã nhận được yêu cầu hủy đơn hàng của bạn và đã xử lý thành công.</p>
                            <p>Nếu bạn vẫn muốn mua sản phẩm, vui lòng đặt hàng lại trên website của chúng tôi.</p>
                            <p>Nếu có thắc mắc hoặc cần hỗ trợ, hãy liên hệ với chúng tôi qua:</p>
                            <ul>
                                <li>📞 Hotline: <strong>0987 654 321</strong></li>
                                <li>📧 Email: <strong>bloomieshop25@gmail.com</strong></li>
                            </ul>
                            <div style='text-align:center; margin: 30px 0;'>
                                <a href='https://bloomie.vn' class='btn'>Tiếp tục mua sắm</a>
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

        // API: Get shipping fee by ward code
        [HttpGet]
        public async Task<IActionResult> GetShippingFee(string wardCode)
        {
            if (string.IsNullOrWhiteSpace(wardCode))
                return Json(new { success = false, message = "Vui lòng chọn phường/xã" });

            var fee = await _shippingService.GetShippingFee(wardCode);
            if (fee == null)
                return Json(new { success = false, message = "Phường/xã này chưa hỗ trợ giao hàng" });

            return Json(new { success = true, fee = fee });
        }

        // GET: Order/RequestReturn/5 - Yêu cầu đổi trả hàng
        [HttpGet]
        public async Task<IActionResult> RequestReturn(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            // Chỉ cho phép đổi trả với đơn hàng đã giao hoặc hoàn thành
            if (order.Status != "Đã giao" && order.Status != "Hoàn thành")
            {
                TempData["error"] = "Chỉ có thể yêu cầu đổi trả với đơn hàng đã giao.";
                return RedirectToAction("Details", new { id });
            }

            // Kiểm tra đã có yêu cầu đổi trả chưa
            var existingReturn = await _context.OrderReturns
                .FirstOrDefaultAsync(r => r.OrderId == id);
            
            if (existingReturn != null)
            {
                TempData["error"] = "Đơn hàng này đã có yêu cầu đổi trả.";
                return RedirectToAction("Details", new { id });
            }

            return View(order);
        }

        // POST: Order/RequestReturn/5 - Xử lý yêu cầu đổi trả
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestReturn(int id, string reason, string returnType, List<IFormFile>? images)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["error"] = "Vui lòng nhập lý do đổi trả.";
                return RedirectToAction("RequestReturn", new { id });
            }

            // Upload images nếu có
            var imageUrls = new List<string>();
            if (images != null && images.Count > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "returns");
                Directory.CreateDirectory(uploadsFolder);

                foreach (var image in images)
                {
                    if (image.Length > 0)
                    {
                        var fileName = $"{Guid.NewGuid()}_{image.FileName}";
                        var filePath = Path.Combine(uploadsFolder, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await image.CopyToAsync(stream);
                        }
                        imageUrls.Add($"/images/returns/{fileName}");
                    }
                }
            }

            // Tạo yêu cầu đổi trả
            var orderReturn = new OrderReturn
            {
                OrderId = id,
                Reason = reason,
                ReturnType = returnType,
                Status = "Chờ xử lý",
                RequestDate = DateTime.Now,
                Images = string.Join(";", imageUrls)
            };

            _context.OrderReturns.Add(orderReturn);
            await _context.SaveChangesAsync();

            // 🔔 GỬI THÔNG BÁO YÊU CẦU HOÀN TRẢ CHO ADMIN
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var customerName = user?.FullName ?? "Khách hàng";
                var returnTypeText = returnType == "Refund" ? "Hoàn tiền" : "Đổi hàng";
                await _notificationService.SendNotificationToAdmins(
                    $"🔄 Yêu cầu {returnTypeText} đơn #{order.OrderId} từ {customerName} - Lý do: {reason.Substring(0, Math.Min(50, reason.Length))}...",
                    "/Admin/AdminOrder/ReturnRequests",
                    "danger"
                );
            }
            catch { }

            TempData["success"] = "Đã gửi yêu cầu đổi trả thành công. Chúng tôi sẽ xử lý trong thời gian sớm nhất.";
            return RedirectToAction("Details", new { id });
        }
    }
}

