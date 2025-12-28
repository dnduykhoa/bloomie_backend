using Bloomie.Data;
using Bloomie.Models.Entities;
// using Bloomie.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Extensions;
using System.Globalization;

namespace Bloomie.Controllers
{
    public class ShoppingCartController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ShoppingCartController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Tạo khóa giỏ hàng duy nhất
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

        // Lấy giỏ hàng từ session, nếu không có thì tạo mới
        private ShoppingCart GetCart()
        {
            var cartKey = GetCartKey();
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey) ?? new ShoppingCart();
            UpdateCartCount(cart);
            return cart;
        }

        // Cập nhật số lượng sản phẩm trong giỏ hàng vào ViewData
        private void UpdateCartCount(ShoppingCart cart)
        {
            // Exclude gift items from cart count
            ViewData["CartCount"] = cart.CartItems?.Where(i => !i.IsGift).Sum(i => i.Quantity) ?? 0;
        }

        [HttpGet]
        public async Task<IActionResult> AddToCart(int productId, int quantity = 1, string? deliveryDate = null, string? deliveryTime = null, decimal? discountedPrice = null)
        {
            // Kiểm tra ngày giao
            DateTime? parsedDeliveryDate = null;
            if (!string.IsNullOrEmpty(deliveryDate))
            {
                if (!DateTime.TryParseExact(deliveryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, message = "Định dạng ngày giao hàng không hợp lệ!" });
                    }
                    TempData["ErrorMessage"] = "Định dạng ngày giao hàng không hợp lệ!";
                    return RedirectToAction("Index");
                }
                parsedDeliveryDate = date;
            }
            else
            {
                parsedDeliveryDate = DateTime.Now.AddDays(1).Date; 
            }

            //Gán khung giờ mặc định nếu không có giá trị
            deliveryTime = string.IsNullOrEmpty(deliveryTime) ? "08:00 - 10:00" : deliveryTime;

            // Kiểm tra sản phẩm (Include Images để hiển thị trong giỏ hàng)
            var product = await _context.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == productId);
                
            if (product == null)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Sản phẩm không tồn tại!" });
                }
                return NotFound();
            }

            // Kiểm tra số lượng
            if (quantity <= 0 || quantity > product.StockQuantity)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Số lượng không hợp lệ hoặc không đủ hàng!" });
                }
                TempData["ErrorMessage"] = "Số lượng không hợp lệ hoặc không đủ hàng!";
                return RedirectToAction("Index");
            }

            // Kiểm tra ngày giao từ hôm nay trở đi
            if (!parsedDeliveryDate.HasValue || parsedDeliveryDate.Value.Date < DateTime.Now.Date)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Ngày giao hàng phải từ hôm nay trở đi!" });
                }
                TempData["ErrorMessage"] = "Ngày giao hàng phải từ hôm nay trở đi!";
                return RedirectToAction("Index");
            }

            // Kiểm tra khung giờ giao
            if (string.IsNullOrEmpty(deliveryTime))
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Vui lòng chọn khung giờ giao hàng!" });
                }
                TempData["ErrorMessage"] = "Vui lòng chọn khung giờ giao hàng!";
                return RedirectToAction("Index");
            }

            // Kiểm tra quyền của người dùng
            var user = await _userManager.GetUserAsync(User);
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
            if (isAuthenticated && user != null && await _userManager.IsInRoleAsync(user, "Admin"))
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Admin không thể thêm sản phẩm vào giỏ hàng!" });
                }
                TempData["ErrorMessage"] = "🚫 Admin không thể thêm sản phẩm vào giỏ hàng.";
                return RedirectToAction("Index");
            }

            // Tính giá giảm cho sản phẩm từ ProductDiscount
            var now = DateTime.Now;
            var activeDiscounts = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .ToListAsync();

            decimal? discountAmount = null;

            foreach (var discount in activeDiscounts)
            {
                bool isApplicable = false;

                // Kiểm tra ApplyTo
                if (discount.ApplyTo == "all")
                {
                    isApplicable = true;
                }
                else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                {
                    var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                    if (productIds != null && productIds.Contains(productId))
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

                    // Chọn giảm giá cao nhất
                    if (tempDiscount > (discountAmount ?? 0))
                    {
                        discountAmount = tempDiscount;
                    }
                }
            }

            // Tạo simplified Product (tránh circular reference khi serialize vào Session)
            var simplifiedProduct = new Product
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price,
                StockQuantity = product.StockQuantity,
                ImageUrl = product.ImageUrl,
                Images = product.Images?.Select(img => new ProductImage
                {
                    Id = img.Id,
                    Url = img.Url,
                    ProductId = img.ProductId,
                    Product = null // Break circular reference
                }).ToList()
            };

            // Tạo đối tượng CartItem với thông tin giảm giá
            var cartItem = new CartItem
            {
                ProductId = productId,
                Quantity = quantity,
                DeliveryDate = parsedDeliveryDate,
                DeliveryTime = deliveryTime,
                Product = simplifiedProduct,
                Discount = discountAmount // Lưu số tiền giảm
            };

            // Thêm vào giỏ hàng và lưu vào session
            var cart = GetCart();
            cart.AddItem(cartItem);
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                // Exclude gift items from cart count
                var totalItems = cart.CartItems?.Where(i => !i.IsGift).Sum(i => i.Quantity) ?? 0;
                return Json(new { success = true, cartCount = totalItems, message = "Đã thêm sản phẩm vào giỏ hàng!" });
            }

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Index()
        {
            var cart = GetCart();
            
            // Nếu giỏ hàng trống hoặc chỉ có gift items → Xóa voucher
            if (cart.CartItems == null || !cart.CartItems.Any() || cart.CartItems.All(i => i.IsGift))
            {
                cart.PromotionCode = null;
                cart.DiscountAmount = null;
                cart.FreeShipping = false;
                cart.GiftProductId = null;
                cart.GiftQuantity = null;
                cart.CartItems = new List<CartItem>(); // Xóa luôn gift items nếu có
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }
            
            // Re-validate voucher nếu đang áp dụng voucher Gift
            if (!string.IsNullOrEmpty(cart.PromotionCode) && cart.CartItems != null && cart.CartItems.Any())
            {
                var promoCode = await _context.PromotionCodes
                    .Include(pc => pc.Promotion)
                        .ThenInclude(p => p!.PromotionGifts)
                    .FirstOrDefaultAsync(pc => pc.Code == cart.PromotionCode);
                
                if (promoCode?.Promotion?.Type == PromotionType.Gift)
                {
                    // Xóa gift items cũ trước khi re-validate
                    cart.CartItems = cart.CartItems.Where(i => !i.IsGift).ToList();
                    
                    // Re-apply voucher để kiểm tra điều kiện
                    ApplyPromotionCode(cart.PromotionCode);
                    cart = GetCart(); // Lấy lại cart sau khi apply
                }
            }
            
            // Load Product data cho tất cả items (bao gồm gift items)
            if (cart.CartItems != null && cart.CartItems.Any())
            {
                var productIds = cart.CartItems.Select(i => i.ProductId).Distinct().ToList();
                var products = await _context.Products
                    .Include(p => p.Images)
                    .Where(p => productIds.Contains(p.Id))
                    .ToListAsync();
                
                foreach (var item in cart.CartItems)
                {
                    if (item.Product == null)
                    {
                        item.Product = products.FirstOrDefault(p => p.Id == item.ProductId)!;
                    }
                }
            }

            // Lấy danh sách voucher khả dụng của user
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userId = _userManager.GetUserId(User);
                var now = DateTime.Now;
                var availableVouchers = await _context.UserVouchers
                    .Include(uv => uv.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                            .ThenInclude(p => p!.PromotionGifts)
                    .Where(uv => uv.UserId == userId 
                        && !uv.IsUsed 
                        && uv.PromotionCode != null
                        && uv.PromotionCode.Promotion != null
                        && uv.PromotionCode.Promotion.IsActive
                        && uv.PromotionCode.Promotion.StartDate <= now
                        && (uv.PromotionCode.Promotion.EndDate == null || uv.PromotionCode.Promotion.EndDate >= now))
                    .ToListAsync();
                
                ViewBag.AvailableVouchers = availableVouchers;
            }
            
            // Hiển thị message từ Session nếu có (từ Gift voucher reload)
            var pendingMessage = HttpContext.Session.GetString("PendingPromotionMessage");
            if (!string.IsNullOrEmpty(pendingMessage))
            {
                TempData["PromotionMessage"] = pendingMessage;
                HttpContext.Session.Remove("PendingPromotionMessage");
            }

            // Cập nhật giá giảm cho các sản phẩm trong giỏ (không động vào logic voucher)
            if (cart.CartItems != null && cart.CartItems.Any())
            {
                var now = DateTime.Now;
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .ToListAsync();

                foreach (var item in cart.CartItems.Where(i => !i.IsGift))
                {
                    decimal? discountAmount = null;

                    foreach (var discount in activeDiscounts)
                    {
                        bool isApplicable = false;

                        // Kiểm tra ApplyTo
                        if (discount.ApplyTo == "all")
                        {
                            isApplicable = true;
                        }
                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                        {
                            var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                            if (productIds != null && productIds.Contains(item.ProductId))
                            {
                                isApplicable = true;
                            }
                        }

                        if (isApplicable)
                        {
                            decimal tempDiscount = 0;
                            var productPrice = item.Product?.Price ?? 0;
                            
                            if (discount.DiscountType == "percent")
                            {
                                tempDiscount = productPrice * (discount.DiscountValue / 100);
                            }
                            else if (discount.DiscountType == "fixed_amount")
                            {
                                tempDiscount = discount.DiscountValue;
                            }

                            // Chọn giảm giá cao nhất
                            if (tempDiscount > (discountAmount ?? 0))
                            {
                                discountAmount = tempDiscount;
                            }
                        }
                    }
                    
                    // Cập nhật discount cho item
                    item.Discount = discountAmount;
                }

                // Xóa circular reference trước khi lưu vào Session
                foreach (var item in cart.CartItems)
                {
                    if (item.Product != null)
                    {
                        item.Product.Images = null;
                    }
                }

                // Lưu lại giỏ hàng với discount đã cập nhật
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }
            
            return View(cart);
        }

        [HttpPost]
        public IActionResult ApplyPromotionCode(string promotionCode)
        {
            
            var cart = GetCart();
            if (string.IsNullOrWhiteSpace(promotionCode))
            {
                TempData["PromotionMessage"] = "Vui lòng nhập mã giảm giá.";
                return RedirectToAction("Index");
            }

            // Đảm bảo cart.CartItems luôn khởi tạo
            if (cart.CartItems == null)
            cart.CartItems = new List<Bloomie.Models.Entities.CartItem>();

            // Tìm mã giảm giá trong DB
            var promoCode = _context.PromotionCodes
                .Where(pc => pc.Code == promotionCode && pc.IsActive && (pc.ExpiryDate == null || pc.ExpiryDate > DateTime.Now))
                .Select(pc => new { pc, promo = pc.Promotion })
                .FirstOrDefault();

            if (promoCode == null)
            {
                TempData["PromotionMessage"] = "Mã giảm giá không hợp lệ hoặc đã hết hạn.";
                cart.PromotionCode = null;
                cart.DiscountAmount = null;
                cart.FreeShipping = false;
                cart.GiftProductId = null;
                cart.GiftQuantity = null;
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                return RedirectToAction("Index");
            }

            // Reset các trường khuyến mãi trước khi áp dụng mới
            cart.PromotionCode = promotionCode;
            cart.DiscountAmount = 0;
            cart.FreeShipping = false;
            cart.GiftProductId = null;
            cart.GiftQuantity = null;
            
            // ⭐ RESET item.Discount về ProductDiscount gốc (tính lại từ database)
            if (cart.CartItems != null)
            {
                var now = DateTime.Now;
                var activeDiscounts = _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .ToList();

                foreach (var item in cart.CartItems.Where(i => !i.IsGift))
                {
                    decimal? discountAmount = null;

                    foreach (var discount in activeDiscounts)
                    {
                        bool isApplicable = false;

                        // Kiểm tra ApplyTo
                        if (discount.ApplyTo == "all")
                        {
                            isApplicable = true;
                        }
                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                        {
                            var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                            if (productIds != null && productIds.Contains(item.ProductId))
                            {
                                isApplicable = true;
                            }
                        }

                        if (isApplicable)
                        {
                            decimal tempDiscount = 0;
                            var productPrice = item.Product?.Price ?? 0;
                            
                            if (discount.DiscountType == "percent")
                            {
                                tempDiscount = productPrice * (discount.DiscountValue / 100);
                            }
                            else if (discount.DiscountType == "fixed_amount")
                            {
                                tempDiscount = discount.DiscountValue;
                            }

                            // Chọn giảm giá cao nhất
                            if (tempDiscount > (discountAmount ?? 0))
                            {
                                discountAmount = tempDiscount;
                            }
                        }
                    }
                    
                    // Reset về ProductDiscount gốc
                    item.Discount = discountAmount;
                }
            }

            var promo = promoCode.promo;
            var code = promoCode.pc;
            
            // Null check cho promo
            if (promo == null)
            {
                TempData["PromotionMessage"] = "Mã giảm giá không hợp lệ.";
                cart.PromotionCode = null;
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                return RedirectToAction("Index");
            }
            
            // Tính total từ giá ĐÃ GIẢM (đã trừ ProductDiscount), không tính gift items
            decimal total = cart.CartItems?
                .Where(i => !i.IsGift)
                .Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity) ?? 0;

            // Kiểm tra UsageLimit (số lần sử dụng tối đa)
            if (code.UsageLimit.HasValue && code.UsedCount >= code.UsageLimit.Value)
            {
                TempData["PromotionMessage"] = "Mã giảm giá đã hết lượt sử dụng.";
                cart.PromotionCode = null;
                cart.DiscountAmount = null;
                cart.FreeShipping = false;
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                return RedirectToAction("Index");
            }

            // Kiểm tra LimitPerCustomer (mỗi khách hàng chỉ dùng 1 lần)
            // TODO: Cần thêm trường PromotionCodeId vào bảng Order để kiểm tra
            /*
            if (code.LimitPerCustomer && User?.Identity?.IsAuthenticated == true)
            {
                var userId = _userManager.GetUserId(User);
                var hasUsed = _context.Orders.Any(o => o.UserId == userId && o.PromotionCodeId == code.Id);
                if (hasUsed)
                {
                    TempData["PromotionMessage"] = "Bạn đã sử dụng mã giảm giá này rồi.";
                    cart.PromotionCode = null;
                    cart.DiscountAmount = null;
                    cart.FreeShipping = false;
                    HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                    return RedirectToAction("Index");
                }
            }
            */

            switch (promo.Type)
            {
                case Bloomie.Models.Entities.PromotionType.Order:
                    // Giảm giá đơn hàng
                    // Kiểm tra MinOrderValue (Tổng giá trị đơn hàng tối thiểu)
                    if (promo.MinOrderValue.HasValue && total < promo.MinOrderValue.Value)
                    {
                        TempData["PromotionMessage"] = $"Đơn hàng phải từ {promo.MinOrderValue.Value:#,##0}đ mới áp dụng được mã này.";
                        break;
                    }
                    
                    // Kiểm tra MinProductQuantity (Tổng số lượng sản phẩm tối thiểu)
                    if (promo.MinProductQuantity.HasValue)
                    {
                        int totalQty = cart.CartItems?.Sum(i => i.Quantity) ?? 0;
                        if (totalQty < promo.MinProductQuantity.Value)
                        {
                            TempData["PromotionMessage"] = $"Đơn hàng phải có tối thiểu {promo.MinProductQuantity.Value} sản phẩm.";
                            break;
                        }
                    }
                    
                    if (code.IsPercent)
                    {
                        var discount = total * (code.Value ?? 0) / 100;
                        if (code.MaxDiscount.HasValue && discount > code.MaxDiscount.Value)
                            discount = code.MaxDiscount.Value;
                        cart.DiscountAmount = discount;
                    }
                    else
                    {
                        // Giảm cố định - vẫn phải kiểm tra MaxDiscount
                        var discount = code.Value ?? 0;
                        if (code.MaxDiscount.HasValue && discount > code.MaxDiscount.Value)
                            discount = code.MaxDiscount.Value;
                        cart.DiscountAmount = discount;
                    }
                    TempData["PromotionMessage"] = $"Áp dụng thành công mã giảm giá đơn hàng.";
                    break;
                case Bloomie.Models.Entities.PromotionType.Product:
                    // Giảm giá sản phẩm dựa trên bảng PromotionProduct và PromotionCategory
                    decimal productDiscountTotal = 0;
                    // Lấy danh sách ProductId áp dụng
                    var promoProductIds = _context.PromotionProducts
                        .Where(pp => pp.PromotionId == promo.Id)
                        .Select(pp => pp.ProductId)
                        .ToList();
                    // Lấy danh sách CategoryId áp dụng
                    var promoCategoryIds = _context.PromotionCategories
                        .Where(pc => pc.PromotionId == promo.Id)
                        .Select(pc => pc.CategoryId)
                        .ToList();
                    // Lọc các CartItem được áp dụng (theo ProductId hoặc Product thuộc CategoryId)
                    var eligibleItems = (cart.CartItems ?? new List<Bloomie.Models.Entities.CartItem>()).Where(item =>
                        promoProductIds.Contains(item.ProductId) ||
                        (item.Product != null && item.Product.ProductCategories != null &&
                            item.Product.ProductCategories.Any(pc => promoCategoryIds.Contains(pc.CategoryId)))
                    ).ToList();
                    
                    // Kiểm tra MinOrderValue (Tổng giá trị đơn hàng tối thiểu)
                    if (promo.MinOrderValue.HasValue && total < promo.MinOrderValue.Value)
                    {
                        TempData["PromotionMessage"] = $"Đơn hàng phải từ {promo.MinOrderValue.Value:#,##0}đ mới áp dụng được mã này.";
                        cart.DiscountAmount = 0;
                        break;
                    }
                    
                    // Kiểm tra điều kiện số lượng tối thiểu (sản phẩm áp dụng)
                    int eligibleQty = eligibleItems.Sum(i => i.Quantity);
                    if (promo.MinProductQuantity.HasValue && eligibleQty < promo.MinProductQuantity.Value)
                    {
                        TempData["PromotionMessage"] = $"Bạn cần mua tối thiểu {promo.MinProductQuantity.Value} sản phẩm áp dụng để dùng mã này.";
                        cart.DiscountAmount = 0;
                        break;
                    }
                    // Kiểm tra điều kiện giá trị tối thiểu (sản phẩm áp dụng) - tính từ giá ĐÃ GIẢM
                    decimal eligibleValue = eligibleItems.Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity);
                    if (promo.MinProductValue.HasValue && eligibleValue < promo.MinProductValue.Value)
                    {
                        TempData["PromotionMessage"] = $"Tổng giá trị sản phẩm áp dụng phải từ {promo.MinProductValue.Value:#,##0}đ.";
                        cart.DiscountAmount = 0;
                        break;
                    }
                    
                    // Tính voucher discount cho từng sản phẩm đủ điều kiện (KHÔNG lưu vào item.Discount)
                    foreach (var item in eligibleItems)
                    {
                        decimal itemDiscount = 0;
                        var originalDiscount = item.Discount ?? 0;
                        var priceAfterProductDiscount = (item.Product?.Price ?? 0) - originalDiscount;
                        
                        if (code.IsPercent)
                        {
                            // Tính % trên giá ĐÃ GIẢM ProductDiscount
                            itemDiscount = priceAfterProductDiscount * (code.Value ?? 0) / 100 * item.Quantity;
                            if (code.MaxDiscount.HasValue && itemDiscount > code.MaxDiscount.Value)
                                itemDiscount = code.MaxDiscount.Value;
                        }
                        else
                        {
                            itemDiscount = (code.Value ?? 0) * item.Quantity;
                        }
                        
                        // CHỈ lưu vào tổng discount, KHÔNG ghi đè item.Discount
                        productDiscountTotal += itemDiscount;
                    }
                    cart.DiscountAmount = productDiscountTotal;
                    TempData["PromotionMessage"] = productDiscountTotal > 0 ? $"Áp dụng thành công mã giảm giá sản phẩm." : $"Không có sản phẩm nào được giảm giá.";
                    break;
                case Bloomie.Models.Entities.PromotionType.Gift:
                    // Mua X tặng Y chuẩn hóa: kiểm tra các bảng liên kết
                    // 1. Lấy các id sản phẩm/danh mục cần mua
                    // Áp dụng logic Mua X tặng Y
                    var giftEntity = _context.PromotionGifts.FirstOrDefault(g => g.PromotionId == promo.Id);
                    if (giftEntity == null)
                    {
                        TempData["PromotionMessage"] = "Chương trình tặng quà không hợp lệ.";
                        break;
                    }

                    // Lấy danh sách sản phẩm/danh mục mua và tặng
                    var buyProductIds = _context.PromotionGiftBuyProducts.Where(x => x.PromotionGiftId == giftEntity.Id).Select(x => x.ProductId).ToList();
                    var buyCategoryIds = _context.PromotionGiftBuyCategories.Where(x => x.PromotionGiftId == giftEntity.Id).Select(x => x.CategoryId).ToList();
                    
                    // ⭐ XÓA gift items CŨ TRƯỚC KHI KIỂM TRA điều kiện
                    cart.CartItems = cart.CartItems?.Where(i => !i.IsGift).ToList();
                    
                    // Tính totalBuyQty từ cart.CartItems ĐÃ XÓA gift items
                    int totalBuyQty = (cart.CartItems ?? new List<Bloomie.Models.Entities.CartItem>())
                        .Where(i => !i.IsGift && (buyProductIds.Contains(i.ProductId) ||
                            (i.Product != null && i.Product.ProductCategories != null &&
                                i.Product.ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))))
                        .Sum(i => i.Quantity);

                    // Điều kiện số lượng tối thiểu
                    if (giftEntity.BuyQuantity > 0 && totalBuyQty < giftEntity.BuyQuantity)
                    {
                        TempData["PromotionMessage"] = $"Bạn cần mua tối thiểu {giftEntity.BuyQuantity} sản phẩm áp dụng để nhận quà tặng.";
                        cart.PromotionCode = null;
                        cart.DiscountAmount = 0;
                        cart.SelectedVoucherId = null;
                        HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                        break;
                    }
                    // Lấy các id sản phẩm/danh mục được tặng
                    var giftProductIds = _context.PromotionGiftGiftProducts.Where(x => x.PromotionGiftId == giftEntity.Id).Select(x => x.ProductId).ToList();
                    var giftCategoryIds = _context.PromotionGiftGiftCategories.Where(x => x.PromotionGiftId == giftEntity.Id).Select(x => x.CategoryId).ToList();

                    // Kiểm tra giá trị tối thiểu của sản phẩm mua (từ cart.CartItems đã xóa gift)
                    decimal totalBuyValue = (cart.CartItems ?? new List<Bloomie.Models.Entities.CartItem>())
                        .Where(i => !i.IsGift && (buyProductIds.Contains(i.ProductId) ||
                            (i.Product != null && i.Product.ProductCategories != null &&
                                i.Product.ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))))
                        .Sum(i => (i.Product?.Price ?? 0) * i.Quantity);
                    if (giftEntity.BuyConditionType == "MinValue" && giftEntity.BuyConditionValueMoney.HasValue && totalBuyValue < giftEntity.BuyConditionValueMoney.Value)
                    {
                        TempData["PromotionMessage"] = $"Tổng giá trị sản phẩm mua phải từ {giftEntity.BuyConditionValueMoney.Value:#,##0}đ.";
                        cart.PromotionCode = null;
                        cart.DiscountAmount = 0;
                        cart.SelectedVoucherId = null;
                        HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                        break;
                    }
                    
                    // Kiểm tra điều kiện MinQuantity
                    if (giftEntity.BuyConditionType == "MinQuantity" && giftEntity.BuyConditionValue.HasValue && totalBuyQty < giftEntity.BuyConditionValue.Value)
                    {
                        TempData["PromotionMessage"] = $"Bạn cần mua tối thiểu {giftEntity.BuyConditionValue.Value} sản phẩm áp dụng để nhận quà tặng.";
                        cart.PromotionCode = null;
                        cart.DiscountAmount = 0;
                        cart.SelectedVoucherId = null;
                        HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                        break;
                    }

                    // Tính số lần đạt điều kiện
                    int timesConditionMet = 0;
                    if (giftEntity.BuyConditionType == "MinQuantity" && giftEntity.BuyConditionValue.HasValue && giftEntity.BuyConditionValue.Value > 0)
                    {
                        timesConditionMet = totalBuyQty / giftEntity.BuyConditionValue.Value;
                    }
                    else if (giftEntity.BuyConditionType == "MinValue" && giftEntity.BuyConditionValueMoney.HasValue && giftEntity.BuyConditionValueMoney.Value > 0)
                    {
                        timesConditionMet = (int)(totalBuyValue / giftEntity.BuyConditionValueMoney.Value);
                    }
                    else
                    {
                        // Fallback cho trường hợp cũ sử dụng BuyQuantity
                        if (giftEntity.BuyQuantity > 0)
                        {
                            timesConditionMet = totalBuyQty / giftEntity.BuyQuantity;
                        }
                        else
                        {
                            timesConditionMet = 1;
                        }
                    }

                    // ⭐ Áp dụng giới hạn LimitPerOrder: chỉ cho phép áp dụng tối đa 1 lần/đơn
                    if (giftEntity.LimitPerOrder && timesConditionMet > 1)
                    {
                        timesConditionMet = 1;
                    }

                    // Tính tổng số lượng sản phẩm tặng dựa trên số lần đạt điều kiện
                    int giftQty = giftEntity.GiftQuantity * timesConditionMet;
                    if (giftQty <= 0)
                    {
                        TempData["PromotionMessage"] = "Bạn cần mua đủ số lượng sản phẩm để nhận quà tặng.";
                        cart.PromotionCode = null;
                        cart.DiscountAmount = 0;
                        cart.SelectedVoucherId = null;
                        HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                        break;
                    }

                    // Lấy danh sách sản phẩm tặng (theo id hoặc category)
                    var giftProducts = _context.Products
                        .Where(p => giftProductIds.Contains(p.Id) ||
                            (p.ProductCategories != null && p.ProductCategories.Any(pc => giftCategoryIds.Contains(pc.CategoryId))))
                        .ToList();
                    if (!giftProducts.Any())
                    {
                        TempData["PromotionMessage"] = "Không tìm thấy sản phẩm tặng phù hợp.";
                        break;
                    }

                    // Loại giảm giá cho sản phẩm tặng: phần trăm, cố định, miễn phí
                    foreach (var giftProduct in giftProducts)
                    {
                        decimal discountPerGift = 0;
                        decimal productDiscount = 0;
                        
                        // Tìm item trong cart để lấy giá đã giảm (nếu có ProductDiscount)
                        var existingItem = cart.CartItems?.FirstOrDefault(i => i.ProductId == giftProduct.Id && !i.IsGift);
                        decimal actualPrice = giftProduct.Price;
                        
                        if (existingItem != null && existingItem.Discount.HasValue && existingItem.Discount.Value > 0)
                        {
                            // Lưu lại ProductDiscount để cộng vào tổng discount sau
                            productDiscount = existingItem.Discount.Value;
                            // Lấy giá sau khi đã giảm ProductDiscount
                            actualPrice = giftProduct.Price - productDiscount;
                        }
                        
                        // Debug: Log để kiểm tra
                        System.Diagnostics.Debug.WriteLine($"Gift Product: {giftProduct.Name}, Price: {giftProduct.Price}, ProductDiscount: {productDiscount}, ActualPrice: {actualPrice}");
                        
                        if (giftEntity.GiftDiscountType == "percent")
                        {
                            // Giảm theo phần trăm dựa trên giá đã giảm
                            discountPerGift = (actualPrice * (giftEntity.GiftDiscountValue ?? 0)) / 100;
                            System.Diagnostics.Debug.WriteLine($"Percent discount: {giftEntity.GiftDiscountValue}% of {actualPrice} = {discountPerGift}");
                        }
                        else if (giftEntity.GiftDiscountType == "money")
                        {
                            // Giảm số tiền cố định
                            discountPerGift = (decimal)(giftEntity.GiftDiscountMoneyValue ?? 0);
                            if (discountPerGift > actualPrice) discountPerGift = actualPrice;
                        }
                        else if (giftEntity.GiftDiscountType == "free")
                        {
                            // Miễn phí hoàn toàn - giảm toàn bộ giá đã giảm
                            discountPerGift = actualPrice;
                        }

                        // Tổng discount = ProductDiscount + Gift voucher discount
                        decimal totalDiscount = productDiscount + discountPerGift;
                        
                        System.Diagnostics.Debug.WriteLine($"Total discount saved: {totalDiscount} (ProductDiscount: {productDiscount} + GiftDiscount: {discountPerGift})");

                        // Thêm sản phẩm tặng vào giỏ hàng
                        if (cart.CartItems == null)
                            cart.CartItems = new List<Bloomie.Models.Entities.CartItem>();
                        
                        // ⭐ Tạo Product object mới với giá đã trừ ProductDiscount và OriginalPrice
                        var giftProductSimple = new Product
                        {
                            Id = giftProduct.Id,
                            Name = giftProduct.Name,
                            Price = actualPrice, // ⭐ Giá sau ProductDiscount (490,000đ)
                            OriginalPrice = giftProduct.Price, // ⭐ Giá gốc (500,000đ)
                            ImageUrl = giftProduct.ImageUrl,
                            StockQuantity = giftProduct.StockQuantity
                        };
                            
                        cart.CartItems.Add(new Bloomie.Models.Entities.CartItem
                        {
                            ProductId = giftProduct.Id,
                            Product = giftProductSimple, // ⭐ Dùng object mới
                            Quantity = giftQty,
                            Discount = discountPerGift, // ⭐ CHỈ lưu GiftDiscount (không bao gồm ProductDiscount)
                            IsGift = true,
                            DeliveryDate = null,
                            DeliveryTime = null
                        });
                    }
                    TempData["PromotionMessage"] = $"Bạn được tặng thêm sản phẩm khi mua đủ số lượng!";
                    break;
                case Bloomie.Models.Entities.PromotionType.Shipping:
                    // Miễn phí vận chuyển
                    // Kiểm tra MinOrderValue (Tổng giá trị đơn hàng tối thiểu)
                    if (promo.MinOrderValue.HasValue && total < promo.MinOrderValue.Value)
                    {
                        TempData["PromotionMessage"] = $"Đơn hàng phải từ {promo.MinOrderValue.Value:#,##0}đ mới áp dụng được mã miễn phí vận chuyển.";
                        break;
                    }
                    
                    // Kiểm tra MinProductValue (Tổng giá trị sản phẩm được khuyến mãi tối thiểu)
                    if (promo.MinProductValue.HasValue)
                    {
                        decimal eligibleProductValue = cart.CartItems?.Sum(i => (i.Product?.Price ?? 0) * i.Quantity) ?? 0;
                        if (eligibleProductValue < promo.MinProductValue.Value)
                        {
                            TempData["PromotionMessage"] = $"Tổng giá trị sản phẩm phải từ {promo.MinProductValue.Value:#,##0}đ.";
                            break;
                        }
                    }
                    
                    // Kiểm tra MinProductQuantity (Tổng số lượng sản phẩm được khuyến mãi tối thiểu)
                    if (promo.MinProductQuantity.HasValue)
                    {
                        int totalProductQty = cart.CartItems?.Sum(i => i.Quantity) ?? 0;
                        if (totalProductQty < promo.MinProductQuantity.Value)
                        {
                            TempData["PromotionMessage"] = $"Đơn hàng phải có tối thiểu {promo.MinProductQuantity.Value} sản phẩm.";
                            break;
                        }
                    }
                    
                    // Xử lý loại giảm giá vận chuyển
                    if (promo.ShippingDiscountType == "free")
                    {
                        cart.FreeShipping = true;
                        TempData["PromotionMessage"] = $"Áp dụng thành công mã miễn phí vận chuyển.";
                    }
                    else if (promo.ShippingDiscountType == "money")
                    {
                        // Giảm số tiền cố định (có thể lưu vào cart.ShippingDiscount nếu cần)
                        cart.FreeShipping = false;
                        // TODO: Implement ShippingDiscount field in cart if needed
                        TempData["PromotionMessage"] = $"Áp dụng thành công mã giảm {promo.ShippingDiscountValue:#,##0}đ phí vận chuyển.";
                    }
                    else if (promo.ShippingDiscountType == "percent")
                    {
                        // Giảm theo phần trăm (có thể lưu vào cart.ShippingDiscountPercent nếu cần)
                        cart.FreeShipping = false;
                        // TODO: Implement ShippingDiscountPercent field in cart if needed
                        TempData["PromotionMessage"] = $"Áp dụng thành công mã giảm {promo.ShippingDiscountValue}% phí vận chuyển.";
                    }
                    else
                    {
                        cart.FreeShipping = true;
                        TempData["PromotionMessage"] = $"Áp dụng thành công mã miễn phí vận chuyển.";
                    }
                    break;                
                default:
                    TempData["PromotionMessage"] = "Mã giảm giá không hợp lệ.";
                    break;
            }

            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> ApplyVoucher(int? selectedVoucherId)
        {
            var cart = GetCart();
            
            if (!selectedVoucherId.HasValue)
            {
                // Xóa voucher đang áp dụng
                cart.PromotionCode = null;
                cart.DiscountAmount = null;
                cart.FreeShipping = false;
                cart.GiftProductId = null;
                cart.GiftQuantity = null;
                
                // Reset discount của các items về giá gốc discount từ ProductDiscount
                if (cart.CartItems != null)
                {
                    var now = DateTime.Now;
                    var activeDiscounts = await _context.ProductDiscounts
                        .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                        .ToListAsync();

                    foreach (var item in cart.CartItems.Where(i => !i.IsGift))
                    {
                        decimal? discountAmount = null;
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
                                if (productIds != null && productIds.Contains(item.ProductId))
                                {
                                    isApplicable = true;
                                }
                            }

                            if (isApplicable)
                            {
                                decimal tempDiscount = 0;
                                var productPrice = item.Product?.Price ?? 0;
                                
                                if (discount.DiscountType == "percent")
                                {
                                    tempDiscount = productPrice * (discount.DiscountValue / 100);
                                }
                                else if (discount.DiscountType == "fixed_amount")
                                {
                                    tempDiscount = discount.DiscountValue;
                                }

                                if (tempDiscount > (discountAmount ?? 0))
                                {
                                    discountAmount = tempDiscount;
                                }
                            }
                        }
                        item.Discount = discountAmount;
                    }
                }
                
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                return RedirectToAction("Index");
            }

            // Lấy voucher từ database
            var userVoucher = await _context.UserVouchers
                .Include(uv => uv.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                .FirstOrDefaultAsync(uv => uv.Id == selectedVoucherId.Value);

            if (userVoucher == null || userVoucher.PromotionCode == null)
            {
                TempData["PromotionMessage"] = "Voucher không tồn tại.";
                return RedirectToAction("Index");
            }

            // Gọi ApplyPromotionCode với mã voucher
            var result = ApplyPromotionCode(userVoucher.PromotionCode.Code);
            
            // ⭐ Nếu áp dụng thành công (không có error message), lưu SelectedVoucherId
            var errorMessage = TempData["PromotionMessage"] as string;
            if (string.IsNullOrEmpty(errorMessage) || !errorMessage.Contains("không") && !errorMessage.Contains("phải"))
            {
                cart = GetCart();
                cart.SelectedVoucherId = selectedVoucherId.Value;
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }
            
            return result;
        }

        [HttpPost]
        public async Task<IActionResult> ApplyVoucherAjax(int? selectedVoucherId)
        {
            var cart = GetCart();
            
            if (!selectedVoucherId.HasValue)
            {
                // Xóa voucher đang áp dụng
                cart.PromotionCode = null;
                cart.DiscountAmount = null;
                cart.FreeShipping = false;
                cart.GiftProductId = null;
                cart.GiftQuantity = null;
                cart.SelectedVoucherId = null; // Xóa voucherId đã chọn
                
                // XÓA TẤT CẢ GIFT ITEMS
                if (cart.CartItems != null && cart.CartItems.Any(i => i.IsGift))
                {
                    cart.CartItems = cart.CartItems.Where(i => !i.IsGift).ToList();
                }
                
                // Reset discount của các items về giá gốc discount từ ProductDiscount
                if (cart.CartItems != null)
                {
                    var now = DateTime.Now;
                    var activeDiscounts = await _context.ProductDiscounts
                        .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                        .ToListAsync();

                    foreach (var item in cart.CartItems.Where(i => !i.IsGift))
                    {
                        decimal? discountAmount = null;
                        foreach (var productDiscount in activeDiscounts)
                        {
                            bool isApplicable = false;
                            if (productDiscount.ApplyTo == "all")
                            {
                                isApplicable = true;
                            }
                            else if (productDiscount.ApplyTo == "products" && !string.IsNullOrEmpty(productDiscount.ProductIds))
                            {
                                var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(productDiscount.ProductIds);
                                if (productIds != null && productIds.Contains(item.ProductId))
                                {
                                    isApplicable = true;
                                }
                            }

                            if (isApplicable)
                            {
                                decimal tempDiscount = 0;
                                var productPrice = item.Product?.Price ?? 0;
                                
                                if (productDiscount.DiscountType == "percent")
                                {
                                    tempDiscount = productPrice * (productDiscount.DiscountValue / 100);
                                }
                                else if (productDiscount.DiscountType == "fixed_amount")
                                {
                                    tempDiscount = productDiscount.DiscountValue;
                                }

                                if (tempDiscount > (discountAmount ?? 0))
                                {
                                    discountAmount = tempDiscount;
                                }
                            }
                        }
                        item.Discount = discountAmount;
                    }
                }
                
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                
                var total = cart.CartItems?.Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity) ?? 0;
                return Json(new { 
                    success = true, 
                    message = "Đã xóa voucher",
                    discountAmount = 0,
                    total = total,
                    finalTotal = total
                });
            }

            // Lấy voucher từ database
            var userVoucher = await _context.UserVouchers
                .Include(uv => uv.PromotionCode)
                    .ThenInclude(pc => pc!.Promotion)
                .FirstOrDefaultAsync(uv => uv.Id == selectedVoucherId.Value);

            if (userVoucher == null || userVoucher.PromotionCode == null)
            {
                return Json(new { success = false, message = "Voucher không tồn tại." });
            }

            // Áp dụng voucher (sử dụng logic ApplyPromotionCode nhưng không redirect)
            var promotionCode = userVoucher.PromotionCode.Code;
            
            // XÓA TẤT CẢ GIFT ITEMS CŨ TRƯỚC KHI ÁP VOUCHER MỚI
            cart = GetCart();
            if (cart.CartItems != null && cart.CartItems.Any(i => i.IsGift))
            {
                cart.CartItems = cart.CartItems.Where(i => !i.IsGift).ToList();
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }
            
            // Gọi ApplyPromotionCode để xử lý logic
            ApplyPromotionCode(promotionCode);
            
            // Lấy cart đã cập nhật
            cart = GetCart();
            
            // Tính toán lại total và discount
            var newTotal = cart.CartItems?
                .Where(i => !i.IsGift)
                .Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity) ?? 0;
            var discount = cart.DiscountAmount ?? 0;
            var newFinalTotal = newTotal - discount;
            if (newFinalTotal < 0) newFinalTotal = 0;
            
            var message = TempData["PromotionMessage"]?.ToString() ?? "Đã áp dụng voucher";
            
            // Kiểm tra xem voucher có được áp dụng thành công không
            // Nếu discount vẫn = 0 hoặc message chứa từ khóa lỗi thì là thất bại
            bool isSuccess = discount > 0 || cart.FreeShipping || (cart.CartItems?.Any(i => i.IsGift) == true);
            
            // Nếu message chứa các từ khóa lỗi thì đánh dấu là thất bại
            var errorKeywords = new[] { "phải từ", "tối thiểu", "không hợp lệ", "đã hết", "không có", "không tìm thấy" };
            if (errorKeywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                isSuccess = false;
            }
            
            // Nếu thành công, lưu SelectedVoucherId
            if (isSuccess)
            {
                cart.SelectedVoucherId = selectedVoucherId.Value;
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }
            
            // Lưu message vào Session để hiển thị sau khi reload (cho Gift voucher)
            if (cart.CartItems?.Any(i => i.IsGift) == true)
            {
                HttpContext.Session.SetString("PendingPromotionMessage", message);
            }
            
            return Json(new { 
                success = isSuccess, 
                message = message,
                discountAmount = discount,
                total = newTotal,
                finalTotal = newFinalTotal,
                hasGiftItems = cart.CartItems?.Any(i => i.IsGift) == true
            });
        }

        public IActionResult RemoveFromCart(int productId, DateTime? deliveryDate, string deliveryTime)
        {
            var cart = GetCart();
            if (cart.CartItems != null)
            {
                // Xóa sản phẩm
                cart.CartItems = cart.CartItems.Where(i => !(i.ProductId == productId && i.DeliveryDate == deliveryDate && i.DeliveryTime == deliveryTime)).ToList();
                
                // Kiểm tra nếu giỏ hàng trống hoặc chỉ còn gift items → Xóa voucher
                var nonGiftItems = cart.CartItems.Where(i => !i.IsGift).ToList();
                if (!nonGiftItems.Any())
                {
                    cart.PromotionCode = null;
                    cart.DiscountAmount = null;
                    cart.FreeShipping = false;
                    cart.GiftProductId = null;
                    cart.GiftQuantity = null;
                    cart.SelectedVoucherId = null;
                    cart.CartItems = nonGiftItems; // Xóa luôn gift items
                    HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                    UpdateCartCount(cart);
                    return RedirectToAction("Index");
                }
                
                // Nếu đang áp voucher Gift, xóa tất cả gift items và re-validate
                if (!string.IsNullOrEmpty(cart.PromotionCode))
                {
                    var promoCode = _context.PromotionCodes
                        .Include(pc => pc.Promotion)
                        .FirstOrDefault(pc => pc.Code == cart.PromotionCode);
                    
                    if (promoCode?.Promotion?.Type == PromotionType.Gift)
                    {
                        // Xóa tất cả gift items
                        cart.CartItems = cart.CartItems.Where(i => !i.IsGift).ToList();
                        
                        // Lưu cart
                        HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                        
                        // Re-apply voucher để kiểm tra điều kiện
                        ApplyPromotionCode(cart.PromotionCode);
                        cart = GetCart(); // Lấy lại sau khi apply
                    }
                }
            }
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            UpdateCartCount(cart);
            return RedirectToAction("Index");
        }

        public IActionResult IncreaseQuantity(int productId)
        {
            var cart = GetCart();
            var item = cart.CartItems?.FirstOrDefault(i => i.ProductId == productId && !i.IsGift);
            if (item != null)
            {
                item.Quantity++;
                
                // Re-validate Gift voucher nếu có
                if (!string.IsNullOrEmpty(cart.PromotionCode))
                {
                    var promoCode = _context.PromotionCodes
                        .Include(pc => pc.Promotion)
                        .FirstOrDefault(pc => pc.Code == cart.PromotionCode);
                    
                    if (promoCode?.Promotion?.Type == PromotionType.Gift)
                    {
                        cart.CartItems = cart.CartItems?.Where(i => !i.IsGift).ToList();
                        HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                        ApplyPromotionCode(cart.PromotionCode);
                        UpdateCartCount(GetCart());
                        return RedirectToAction("Index");
                    }
                }
                
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                UpdateCartCount(cart);
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult IncreaseQuantityAjax(int productId)
        {
            var cart = GetCart();
            var item = cart.CartItems?.FirstOrDefault(i => i.ProductId == productId && !i.IsGift);
            if (item == null)
            {
                return Json(new { success = false, message = "Sản phẩm không tồn tại" });
            }

            item.Quantity++;
            
            // Re-apply voucher nếu có để tính lại discount
            bool needReload = false;
            var oldPromotionCode = cart.PromotionCode;
            
            if (!string.IsNullOrEmpty(cart.PromotionCode))
            {
                var promoCode = _context.PromotionCodes
                    .Include(pc => pc.Promotion)
                    .FirstOrDefault(pc => pc.Code == cart.PromotionCode);
                
                if (promoCode?.Promotion?.Type == PromotionType.Gift)
                {
                    needReload = true;
                }
                
                // Lưu cart trước khi re-apply (với quantity đã tăng)
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                
                // Re-apply voucher để tính lại discount với số lượng mới
                ApplyPromotionCode(cart.PromotionCode);
                cart = GetCart(); // Lấy lại cart sau khi apply
            }
            else
            {
                // Không có voucher, chỉ cần lưu session
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }
            
            UpdateCartCount(cart);
            
            // Kiểm tra xem voucher có bị xóa không
            bool voucherCleared = !string.IsNullOrEmpty(oldPromotionCode) && string.IsNullOrEmpty(cart.PromotionCode);
            
            // Lấy message từ TempData (nếu có từ ApplyPromotionCode)
            var promotionMessage = TempData["PromotionMessage"]?.ToString();
            
            // Tính toán lại totals
            var total = cart.CartItems?
                .Where(i => !i.IsGift)
                .Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity) ?? 0;
            var discount = cart.DiscountAmount ?? 0;
            var finalTotal = total - discount;
            if (finalTotal < 0) finalTotal = 0;
            
            return Json(new { 
                success = true, 
                needReload = needReload,
                quantity = item.Quantity,
                total = total,
                discount = discount,
                finalTotal = finalTotal,
                itemTotal = ((item.Product?.Price ?? 0) - (item.Discount ?? 0)) * item.Quantity,
                voucherMessage = promotionMessage,
                voucherCleared = voucherCleared
            });
        }

        [HttpPost]
        public IActionResult DecreaseQuantityAjax(int productId)
        {
            var cart = GetCart();
            var item = cart.CartItems?.FirstOrDefault(i => i.ProductId == productId && !i.IsGift);
            if (item == null)
            {
                return Json(new { success = false, message = "Sản phẩm không tồn tại" });
            }

            if (item.Quantity > 1)
            {
                item.Quantity--;
            }
            else
            {
                // Xóa sản phẩm nếu quantity = 0
                cart.CartItems?.Remove(item);
            }
            
            // KHÔNG LƯU SESSION Ở ĐÂY - để ApplyPromotionCode xử lý
            // HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            
            // Re-apply voucher nếu có để tính lại discount
            bool needReload = false;
            var oldPromotionCode = cart.PromotionCode;
            
            if (!string.IsNullOrEmpty(cart.PromotionCode))
            {
                var promoCode = _context.PromotionCodes
                    .Include(pc => pc.Promotion)
                    .FirstOrDefault(pc => pc.Code == cart.PromotionCode);
                
                if (promoCode?.Promotion?.Type == PromotionType.Gift)
                {
                    needReload = true;
                }
                
                // Lưu cart trước khi re-apply (với quantity đã giảm)
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                
                // Re-apply voucher để tính lại discount với số lượng mới
                ApplyPromotionCode(cart.PromotionCode);
                cart = GetCart(); // Lấy lại cart sau khi apply
            }
            else
            {
                // Không có voucher, chỉ cần lưu session
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }
            
            UpdateCartCount(cart);
            
            // Kiểm tra xem voucher có bị xóa không
            bool voucherCleared = !string.IsNullOrEmpty(oldPromotionCode) && string.IsNullOrEmpty(cart.PromotionCode);
            
            // Lấy message từ TempData (nếu có từ ApplyPromotionCode)
            var promotionMessage = TempData["PromotionMessage"]?.ToString();
            
            // Tính toán lại totals
            var total = cart.CartItems?
                .Where(i => !i.IsGift)
                .Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity) ?? 0;
            var discount = cart.DiscountAmount ?? 0;
            var finalTotal = total - discount;
            if (finalTotal < 0) finalTotal = 0;
            
            var itemTotal = item.Quantity > 0 ? ((item.Product?.Price ?? 0) - (item.Discount ?? 0)) * item.Quantity : 0;
            
            return Json(new { 
                success = true, 
                needReload = needReload,
                voucherCleared = voucherCleared,
                quantity = item.Quantity,
                total = total,
                discount = discount,
                finalTotal = finalTotal,
                itemTotal = itemTotal,
                removed = item.Quantity == 0,
                voucherMessage = promotionMessage
            });
        }

        public IActionResult DecreaseQuantity(int productId)
        {
            var cart = GetCart();
            var item = cart.CartItems?.FirstOrDefault(i => i.ProductId == productId && !i.IsGift);
            if (item != null)
            {
                if (item.Quantity > 1)
                {
                    item.Quantity--;
                }
                else if (cart.CartItems != null)
                {
                    cart.CartItems.Remove(item);
                }
                
                // Re-validate Gift voucher nếu có
                if (!string.IsNullOrEmpty(cart.PromotionCode))
                {
                    var promoCode = _context.PromotionCodes
                        .Include(pc => pc.Promotion)
                        .FirstOrDefault(pc => pc.Code == cart.PromotionCode);
                    
                    if (promoCode?.Promotion?.Type == PromotionType.Gift)
                    {
                        cart.CartItems = cart.CartItems?.Where(i => !i.IsGift).ToList();
                        HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                        ApplyPromotionCode(cart.PromotionCode);
                        UpdateCartCount(GetCart());
                        return RedirectToAction("Index");
                    }
                }
                
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                UpdateCartCount(cart);
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult UpdateQuantity(int productId, int quantity)
        {
            var cart = GetCart();
            var item = cart.CartItems?.FirstOrDefault(i => i.ProductId == productId);
            if (item == null)
            {
                return Json(new { success = false, message = "Sản phẩm không tồn tại trong giỏ hàng!" });
            }
            if (quantity < 1)
            {
                quantity = 1;
            }
            item.Quantity = quantity;
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            // Calculate new total using Product.Price
            var newTotal = cart.CartItems?.Sum(i => (i.Product?.Price ?? 0) * i.Quantity) ?? 0;
            return Json(new { success = true, newTotal = newTotal });
        }

        [HttpPost]
        public IActionResult UpdateDeliveryInfo(int productId, string deliveryDate, string deliveryTime)
        {
            try
            {
                var cart = GetCart();
                if (cart.CartItems != null)
                {
                    var item = cart.CartItems.FirstOrDefault(i => i.ProductId == productId && !i.IsGift);
                    if (item != null)
                    {
                        DateTime parsedDate;
                        if (DateTime.TryParseExact(deliveryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        {
                            item.DeliveryDate = parsedDate;
                        }
                        if (!string.IsNullOrEmpty(deliveryTime))
                        {
                            item.DeliveryTime = deliveryTime;
                        }
                        HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
                        
                        // Trả về JSON response cho AJAX request
                        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.ContentType?.Contains("application/json") == true)
                        {
                            return Json(new { success = true, message = "Cập nhật thành công" });
                        }
                        
                        return Json(new { success = true, message = "Cập nhật thành công" });
                    }
                }
                
                return Json(new { success = false, message = "Không tìm thấy sản phẩm trong giỏ hàng" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult CheckCart()
        {
            var cart = GetCart();
            bool hasItems = cart.CartItems != null && cart.CartItems.Any();
            return Json(hasItems);
        }
    }
}