using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Bloomie.Extensions;
using System.Globalization;

namespace Bloomie.ApiControllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ShoppingCartApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ShoppingCartApiController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ⭐ CHUYỂN SANG DATABASE - Không còn dùng Session
        // Helper: Lấy userId từ claims
        private string? GetCurrentUserId()
        {
            return User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        // GET: api/ShoppingCartApi
        // ⭐ Lấy giỏ hàng từ DATABASE
        [HttpGet]
        public async Task<IActionResult> GetCart()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
            }

            // ⭐ 1. Lấy cart items từ database
            var dbCartItems = await _context.CartItems
                .Include(c => c.Product)
                    .ThenInclude(p => p!.Images)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            // ⭐ 2. Lấy cart state (voucher đã lưu)
            var cartState = await _context.UserCartStates
                .FirstOrDefaultAsync(s => s.UserId == userId);

            // ⭐ 3. Validate lại voucher nếu có
            if (cartState != null && !string.IsNullOrEmpty(cartState.PromotionCode))
            {
                var isValid = await ValidatePromotionCode(cartState.PromotionCode, dbCartItems);
                if (!isValid)
                {
                    // Voucher đã hết hạn → Xóa và thông báo
                    cartState.PromotionCode = null;
                    cartState.DiscountAmount = 0;
                    cartState.FreeShipping = false;
                    _context.UserCartStates.Update(cartState);
                    await _context.SaveChangesAsync();
                }
            }

            // Chuyển sang ShoppingCart model để tương thích với logic cũ
            var cart = new ShoppingCart
            {
                CartItems = dbCartItems,
                PromotionCode = cartState?.PromotionCode,
                DiscountAmount = cartState?.DiscountAmount,
                FreeShipping = cartState?.FreeShipping ?? false
            };

            // Cập nhật giá giảm cho các sản phẩm trong giỏ
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

                // ⭐ L\u01b0u discount v\u00e0o DATABASE thay v\u00ec Session
                _context.CartItems.UpdateRange(cart.CartItems.Where(i => !i.IsGift));
                await _context.SaveChangesAsync();
            }

            var cartCount = cart.CartItems?.Where(i => !i.IsGift).Sum(i => i.Quantity) ?? 0;
            
            // ⭐ BƯỚC 1: Tính total sản phẩm thường (sau khi trừ ProductDiscount)
            var total = cart.CartItems?.Where(i => !i.IsGift).Sum(i => (i.Product?.Price ?? 0) * i.Quantity) ?? 0;
            var totalDiscount = cart.CartItems?.Where(i => !i.IsGift).Sum(i => (i.Discount ?? 0) * i.Quantity) ?? 0;

            // ⭐ BƯỚC 2: Tính total gift items (sau khi trừ tất cả discount)
            var giftItemsTotal = cart.CartItems?.Where(i => i.IsGift)
                .Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity) ?? 0;

            // ⭐ BƯỚC 3: Tính tổng GiftDiscount (chỉ phần giảm giá từ voucher, không bao gồm ProductDiscount)
            var totalGiftDiscount = 0m;
            if (cart.CartItems != null)
            {
                foreach (var item in cart.CartItems.Where(i => i.IsGift))
                {
                    if (!string.IsNullOrEmpty(item.Note))
                    {
                        var parts = item.Note.Split('|');
                        if (parts.Length == 2 && decimal.TryParse(parts[1], out var giftDiscount))
                        {
                            totalGiftDiscount += giftDiscount * item.Quantity;
                        }
                    }
                }
            }

            // ⭐ BƯỚC 4: Tính finalTotal = (sản phẩm thường - discount sản phẩm thường - discount voucher) + gift items
            var finalTotal = (total - totalDiscount - (cart.DiscountAmount ?? 0)) + giftItemsTotal;

            // Tạo response với computed properties cho Flutter - CHỈ sản phẩm thường
            var cartItemsResponse = cart.CartItems?.Where(i => !i.IsGift).Select(item => new
            {
                productId = item.ProductId,
                quantity = item.Quantity,
                deliveryDate = item.DeliveryDate,
                deliveryTime = item.DeliveryTime,
                note = item.Note,
                isGift = item.IsGift,
                discount = item.Discount,
                product = item.Product == null ? null : new
                {
                    id = item.Product.Id,
                    name = item.Product.Name,
                    description = item.Product.Description,
                    price = item.Product.Price,
                    discountedPrice = item.Product.Price - (item.Discount ?? 0), // ⭐ Giá sau giảm
                    hasDiscount = item.Discount.HasValue && item.Discount.Value > 0, // ⭐ Có giảm giá?
                    imageUrl = item.Product.ImageUrl,
                    stockQuantity = item.Product.StockQuantity,
                    isActive = item.Product.IsActive,
                    images = item.Product.Images?.Select(img => new
                    {
                        id = img.Id,
                        url = img.Url,
                        productId = img.ProductId
                    }).ToList()
                }
            }).ToList();

            // ⭐ Tạo response cho sản phẩm tặng
            var giftItemsResponse = new List<object>();
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
                    
                    giftItemsResponse.Add(new
                    {
                        productId = item.ProductId,
                        quantity = item.Quantity,
                        productName = item.Product?.Name ?? "",
                        productImage = item.Product?.ImageUrl ?? "",
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
                cart = new
                {
                    cartItems = cartItemsResponse,
                    promotionCode = cart.PromotionCode,
                    discountAmount = cart.DiscountAmount,
                    freeShipping = cart.FreeShipping
                },
                giftItems = giftItemsResponse,
                hasGiftItems = giftItemsResponse.Any(),
                cartCount = cartCount,
                total = total,
                totalDiscount = totalDiscount,
                giftItemsTotal = giftItemsTotal,  // ⭐ Thêm field mới
                totalGiftDiscount = totalGiftDiscount,  // ⭐ Thêm field này
                finalTotal = finalTotal  // ⭐ Đã bao gồm gift items
            });
        }

        // POST: api/ShoppingCartApi/add
        [HttpPost("add")]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
        {
            if (request == null || request.ProductId <= 0 || request.Quantity <= 0)
            {
                return BadRequest(new { success = false, message = "Thông tin sản phẩm không hợp lệ!" });
            }

            // Kiểm tra ngày giao
            DateTime? parsedDeliveryDate = null;
            if (!string.IsNullOrEmpty(request.DeliveryDate))
            {
                if (!DateTime.TryParseExact(request.DeliveryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return BadRequest(new { success = false, message = "Định dạng ngày giao hàng không hợp lệ!" });
                }
                parsedDeliveryDate = date;
            }
            else
            {
                parsedDeliveryDate = DateTime.Now.AddDays(1).Date;
            }

            // Gán khung giờ mặc định
            var deliveryTime = string.IsNullOrEmpty(request.DeliveryTime) ? "08:00 - 10:00" : request.DeliveryTime;

            // Kiểm tra sản phẩm
            var product = await _context.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId);
                
            if (product == null)
            {
                return NotFound(new { success = false, message = "Sản phẩm không tồn tại!" });
            }

            // Kiểm tra số lượng
            if (request.Quantity > product.StockQuantity)
            {
                return BadRequest(new { success = false, message = "Số lượng không đủ hàng!" });
            }

            // Kiểm tra ngày giao từ hôm nay trở đi
            if (!parsedDeliveryDate.HasValue || parsedDeliveryDate.Value.Date < DateTime.Now.Date)
            {
                return BadRequest(new { success = false, message = "Ngày giao hàng phải từ hôm nay trở đi!" });
            }

            // Kiểm tra quyền của người dùng
            var user = await _userManager.GetUserAsync(User);
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
            if (isAuthenticated && user != null && await _userManager.IsInRoleAsync(user, "Admin"))
            {
                return BadRequest(new { success = false, message = "Admin không thể thêm sản phẩm vào giỏ hàng!" });
            }

            // Tính giá giảm cho sản phẩm
            var now = DateTime.Now;
            var activeDiscounts = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .ToListAsync();

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
                    if (productIds != null && productIds.Contains(request.ProductId))
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

                    if (tempDiscount > (discountAmount ?? 0))
                    {
                        discountAmount = tempDiscount;
                    }
                }
            }

            // Tạo simplified Product
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
                    Product = null
                }).ToList()
            };

            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
            }

            // ⭐ THÊM VÀO DATABASE thay vì Session
            // Kiểm tra đã có trong giỏ chưa
            var existingItem = await _context.CartItems
                .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == request.ProductId);

            if (existingItem != null)
            {
                // Cập nhật số lượng
                var newQuantity = existingItem.Quantity + request.Quantity;
                if (newQuantity > product.StockQuantity)
                {
                    return BadRequest(new { success = false, message = $"Không thể thêm. Sản phẩm chỉ còn {product.StockQuantity} sản phẩm" });
                }

                existingItem.Quantity = newQuantity;
                existingItem.DeliveryDate = parsedDeliveryDate;
                existingItem.DeliveryTime = deliveryTime;
                existingItem.Discount = discountAmount;

                _context.CartItems.Update(existingItem);
            }
            else
            {
                // Thêm mới
                var newCartItem = new CartItem
                {
                    UserId = userId,
                    ProductId = request.ProductId,
                    Quantity = request.Quantity,
                    DeliveryDate = parsedDeliveryDate,
                    DeliveryTime = deliveryTime,
                    Discount = discountAmount,
                    IsGift = false
                };

                _context.CartItems.Add(newCartItem);
            }

            await _context.SaveChangesAsync();

            // Đếm tổng số items
            var totalItems = await _context.CartItems
                .Where(c => c.UserId == userId && !c.IsGift)
                .SumAsync(c => c.Quantity);
            
            return Ok(new
            {
                success = true,
                cartCount = totalItems,
                message = "Đã thêm sản phẩm vào giỏ hàng!"
            });
        }

        // POST: api/ShoppingCartApi/apply-promotion
        [HttpPost("apply-promotion")]
        public async Task<IActionResult> ApplyPromotionCode([FromBody] ApplyPromotionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.PromotionCode))
            {
                return BadRequest(new { success = false, message = "Vui lòng nhập mã giảm giá." });
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
            }

            // ⭐ Lấy cart từ DATABASE
            var dbCartItems = await _context.CartItems
                .Include(c => c.Product)
                    .ThenInclude(p => p!.Images)
                .Include(c => c.Product)
                    .ThenInclude(p => p!.ProductCategories)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            // ⭐ XÓA TẤT CẢ GIFT ITEMS KHI APPLY VOUCHER MỚI
            var giftItems = dbCartItems.Where(i => i.IsGift).ToList();
            if (giftItems.Any())
            {
                _context.CartItems.RemoveRange(giftItems);
                dbCartItems = dbCartItems.Where(i => !i.IsGift).ToList();
            }

            // Chuyển sang ShoppingCart model để tương thích với logic cũ
            var cart = new ShoppingCart
            {
                CartItems = dbCartItems
            };

            // ⭐ Load ProductDiscounts để tính cho gift items
            var now = DateTime.Now;
            var activeDiscounts = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .ToListAsync();

            // Tìm mã giảm giá trong DB
            var promoCode = _context.PromotionCodes
                .Where(pc => pc.Code == request.PromotionCode && pc.IsActive && (pc.ExpiryDate == null || pc.ExpiryDate > DateTime.Now))
                .Select(pc => new { pc, promo = pc.Promotion })
                .FirstOrDefault();

            if (promoCode == null)
            {
                // Không cần reset gì vì chưa áp dụng
                return BadRequest(new { success = false, message = "Mã giảm giá không hợp lệ hoặc đã hết hạn." });
            }

            // Reset các trường khuyến mãi
            cart.PromotionCode = request.PromotionCode;
            cart.DiscountAmount = 0;
            cart.FreeShipping = false;
            cart.GiftProductId = null;
            cart.GiftQuantity = null;

            var promo = promoCode.promo;
            var code = promoCode.pc;
            
            if (promo == null)
            {
                return BadRequest(new { success = false, message = "Mã giảm giá không hợp lệ." });
            }
            
            // ⭐ Tính total trên giá SAU KHI ĐÃ TRỪ ProductDiscount
            decimal total = cart.CartItems?.Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity) ?? 0;

            // Kiểm tra UsageLimit
            if (code.UsageLimit.HasValue && code.UsedCount >= code.UsageLimit.Value)
            {
                return BadRequest(new { success = false, message = "Mã giảm giá đã hết lượt sử dụng." });
            }

            string message = "";

            switch (promo.Type)
            {
                case PromotionType.Order:
                    // Giảm giá đơn hàng
                    if (promo.MinOrderValue.HasValue && total < promo.MinOrderValue.Value)
                    {
                        message = $"Đơn hàng phải từ {promo.MinOrderValue.Value:#,##0}đ mới áp dụng được mã này.";
                        break;
                    }
                    
                    if (promo.MinProductQuantity.HasValue)
                    {
                        int totalQty = cart.CartItems?.Sum(i => i.Quantity) ?? 0;
                        if (totalQty < promo.MinProductQuantity.Value)
                        {
                            message = $"Đơn hàng phải có tối thiểu {promo.MinProductQuantity.Value} sản phẩm.";
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
                        cart.DiscountAmount = code.Value ?? 0;
                    }
                    message = "Áp dụng thành công mã giảm giá đơn hàng.";
                    break;

                case PromotionType.Product:
                    // Giảm giá sản phẩm
                    decimal productDiscountTotal = 0;
                    var promoProductIds = _context.PromotionProducts
                        .Where(pp => pp.PromotionId == promo.Id)
                        .Select(pp => pp.ProductId)
                        .ToList();
                    var promoCategoryIds = _context.PromotionCategories
                        .Where(pc => pc.PromotionId == promo.Id)
                        .Select(pc => pc.CategoryId)
                        .ToList();
                    
                    var eligibleItems = (cart.CartItems ?? new List<CartItem>()).Where(item =>
                        promoProductIds.Contains(item.ProductId) ||
                        (item.Product != null && item.Product.ProductCategories != null &&
                            item.Product.ProductCategories.Any(pc => promoCategoryIds.Contains(pc.CategoryId)))
                    ).ToList();
                    
                    if (promo.MinOrderValue.HasValue && total < promo.MinOrderValue.Value)
                    {
                        message = $"Đơn hàng phải từ {promo.MinOrderValue.Value:#,##0}đ mới áp dụng được mã này.";
                        cart.DiscountAmount = 0;
                        break;
                    }
                    
                    int eligibleQty = eligibleItems.Sum(i => i.Quantity);
                    if (promo.MinProductQuantity.HasValue && eligibleQty < promo.MinProductQuantity.Value)
                    {
                        message = $"Bạn cần mua tối thiểu {promo.MinProductQuantity.Value} sản phẩm áp dụng để dùng mã này.";
                        cart.DiscountAmount = 0;
                        break;
                    }
                    
                    // ⭐ Tính eligibleValue trên giá SAU ProductDiscount
                    decimal eligibleValue = eligibleItems.Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity);
                    if (promo.MinProductValue.HasValue && eligibleValue < promo.MinProductValue.Value)
                    {
                        message = $"Tổng giá trị sản phẩm áp dụng phải từ {promo.MinProductValue.Value:#,##0}đ.";
                        cart.DiscountAmount = 0;
                        break;
                    }
                    
                    // ⚠️ KHÔNG reset item.Discount về 0 vì đó là ProductDiscount
                    // item.Discount giữ nguyên ProductDiscount, promotion discount tính riêng
                    
                    foreach (var item in eligibleItems)
                    {
                        decimal promotionDiscount = 0;
                        // Tính promotion discount trên giá SAU KHI ĐÃ TRỪ ProductDiscount
                        var priceAfterProductDiscount = (item.Product?.Price ?? 0) - (item.Discount ?? 0);
                        
                        if (code.IsPercent)
                        {
                            promotionDiscount = priceAfterProductDiscount * (code.Value ?? 0) / 100 * item.Quantity;
                            if (code.MaxDiscount.HasValue && promotionDiscount > code.MaxDiscount.Value)
                                promotionDiscount = code.MaxDiscount.Value;
                        }
                        else
                        {
                            promotionDiscount = (code.Value ?? 0) * item.Quantity;
                        }
                        productDiscountTotal += promotionDiscount;
                    }
                    cart.DiscountAmount = productDiscountTotal;
                    message = productDiscountTotal > 0 ? "Áp dụng thành công mã giảm giá sản phẩm." : "Không có sản phẩm nào được giảm giá.";
                    break;

                case PromotionType.Gift:
                    // Mua X tặng Y
                    var giftEntity = _context.PromotionGifts.FirstOrDefault(g => g.PromotionId == promo.Id);
                    if (giftEntity == null)
                    {
                        message = "Chương trình tặng quà không hợp lệ.";
                        break;
                    }

                    var buyProductIds = _context.PromotionGiftBuyProducts.Where(x => x.PromotionGiftId == giftEntity.Id).Select(x => x.ProductId).ToList();
                    var buyCategoryIds = _context.PromotionGiftBuyCategories.Where(x => x.PromotionGiftId == giftEntity.Id).Select(x => x.CategoryId).ToList();
                    var cartItems = cart.CartItems ?? new List<CartItem>();
                    int totalBuyQty = cartItems
                        .Where(i => buyProductIds.Contains(i.ProductId) ||
                            (i.Product != null && i.Product.ProductCategories != null &&
                                i.Product.ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId))))
                        .Sum(i => i.Quantity);

                    if (giftEntity.BuyQuantity > 0 && totalBuyQty < giftEntity.BuyQuantity)
                    {
                        message = $"Bạn cần mua tối thiểu {giftEntity.BuyQuantity} sản phẩm áp dụng để nhận quà tặng.";
                        break;
                    }

                    var giftProductIds = _context.PromotionGiftGiftProducts.Where(x => x.PromotionGiftId == giftEntity.Id).Select(x => x.ProductId).ToList();
                    var giftCategoryIds = _context.PromotionGiftGiftCategories.Where(x => x.PromotionGiftId == giftEntity.Id).Select(x => x.CategoryId).ToList();

                    cart.CartItems = cart.CartItems?.Where(i => !i.IsGift).ToList();

                    // ⭐ Tính totalBuyValue trên giá SAU ProductDiscount
                    decimal totalBuyValue = cartItems
                        .Where(i => buyProductIds.Contains(i.ProductId) ||
                            (i.Product != null && i.Product.ProductCategories != null &&
                                i.Product.ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId))))
                        .Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity);
                    
                    if (giftEntity.BuyConditionType == "MinValue" && giftEntity.BuyConditionValueMoney.HasValue && totalBuyValue < giftEntity.BuyConditionValueMoney.Value)
                    {
                        message = $"Tổng giá trị sản phẩm mua phải từ {giftEntity.BuyConditionValueMoney.Value:#,##0}đ.";
                        break;
                    }

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
                        if (giftEntity.BuyQuantity > 0)
                        {
                            timesConditionMet = totalBuyQty / giftEntity.BuyQuantity;
                        }
                        else
                        {
                            timesConditionMet = 1;
                        }
                    }

                    if (giftEntity.LimitPerOrder && timesConditionMet > 1)
                    {
                        timesConditionMet = 1;
                    }

                    int giftQty = giftEntity.GiftQuantity * timesConditionMet;
                    if (giftQty <= 0)
                    {
                        message = "Chương trình tặng quà không hợp lệ (số lượng tặng = 0).";
                        break;
                    }

                    var giftProducts = _context.Products
                        .Where(p => giftProductIds.Contains(p.Id) ||
                            (p.ProductCategories != null && p.ProductCategories.Any(pc => giftCategoryIds.Contains(pc.CategoryId))))
                        .ToList();
                    
                    if (!giftProducts.Any())
                    {
                        message = "Không tìm thấy sản phẩm tặng phù hợp.";
                        break;
                    }

                    foreach (var giftProduct in giftProducts)
                    {
                        // ⭐ BƯỚC 1: Tìm ProductDiscount cho gift item (nếu có)
                        decimal productDiscount = 0;
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
                                if (discountProductIds != null && discountProductIds.Contains(giftProduct.Id))
                                {
                                    isApplicable = true;
                                }
                            }
                            
                            if (isApplicable)
                            {
                                decimal tempDiscount = 0;
                                
                                if (discount.DiscountType == "percent")
                                {
                                    tempDiscount = giftProduct.Price * (discount.DiscountValue / 100);
                                }
                                else if (discount.DiscountType == "fixed_amount")
                                {
                                    tempDiscount = discount.DiscountValue;
                                }
                                
                                if (tempDiscount > productDiscount)
                                {
                                    productDiscount = tempDiscount;
                                }
                            }
                        }
                        
                        // ⭐ BƯỚC 2: Tính giá sau ProductDiscount
                        decimal priceAfterProductDiscount = giftProduct.Price - productDiscount;
                        
                        // ⭐ BƯỚC 3: Tính GiftDiscount trên giá SAU ProductDiscount
                        decimal giftDiscount = 0;
                        if (giftEntity.GiftDiscountType == "percent")
                        {
                            giftDiscount = priceAfterProductDiscount * ((giftEntity.GiftDiscountValue ?? 0m) / 100m);
                        }
                        else if (giftEntity.GiftDiscountType == "money")
                        {
                            giftDiscount = (decimal)(giftEntity.GiftDiscountMoneyValue ?? 0);
                            if (giftDiscount > priceAfterProductDiscount) giftDiscount = priceAfterProductDiscount;
                        }
                        else if (giftEntity.GiftDiscountType == "free")
                        {
                            giftDiscount = priceAfterProductDiscount;
                        }

                        if (cart.CartItems == null)
                            cart.CartItems = new List<CartItem>();
                            
                        // ⭐ BƯỚC 4: Tổng discount = ProductDiscount + GiftDiscount
                        decimal totalDiscount = productDiscount + giftDiscount;
                            
                        cart.CartItems.Add(new CartItem
                        {
                            ProductId = giftProduct.Id,
                            Product = giftProduct,
                            Quantity = giftQty,
                            Discount = totalDiscount,  // ⭐ Lưu tổng discount
                            IsGift = true,
                            DeliveryDate = null,
                            DeliveryTime = null,
                            Note = $"{productDiscount}|{giftDiscount}"  // ⭐ Lưu riêng để debug
                        });
                    }
                    message = "Bạn được tặng thêm sản phẩm khi mua đủ số lượng!";
                    break;

                case PromotionType.Shipping:
                    // Miễn phí vận chuyển
                    if (promo.MinOrderValue.HasValue && total < promo.MinOrderValue.Value)
                    {
                        message = $"Đơn hàng phải từ {promo.MinOrderValue.Value:#,##0}đ mới áp dụng được mã miễn phí vận chuyển.";
                        break;
                    }
                    
                    if (promo.MinProductValue.HasValue)
                    {
                        // ⭐ Tính eligibleProductValue trên giá SAU ProductDiscount
                        decimal eligibleProductValue = cart.CartItems?.Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity) ?? 0;
                        if (eligibleProductValue < promo.MinProductValue.Value)
                        {
                            message = $"Tổng giá trị sản phẩm phải từ {promo.MinProductValue.Value:#,##0}đ.";
                            break;
                        }
                    }
                    
                    if (promo.MinProductQuantity.HasValue)
                    {
                        int totalProductQty = cart.CartItems?.Sum(i => i.Quantity) ?? 0;
                        if (totalProductQty < promo.MinProductQuantity.Value)
                        {
                            message = $"Đơn hàng phải có tối thiểu {promo.MinProductQuantity.Value} sản phẩm.";
                            break;
                        }
                    }
                    
                    if (promo.ShippingDiscountType == "free")
                    {
                        cart.FreeShipping = true;
                        message = "Áp dụng thành công mã miễn phí vận chuyển.";
                    }
                    else if (promo.ShippingDiscountType == "money")
                    {
                        cart.FreeShipping = false;
                        message = $"Áp dụng thành công mã giảm {promo.ShippingDiscountValue:#,##0}đ phí vận chuyển.";
                    }
                    else if (promo.ShippingDiscountType == "percent")
                    {
                        cart.FreeShipping = false;
                        message = $"Áp dụng thành công mã giảm {promo.ShippingDiscountValue}% phí vận chuyển.";
                    }
                    else
                    {
                        cart.FreeShipping = true;
                        message = "Áp dụng thành công mã miễn phí vận chuyển.";
                    }
                    break;

                default:
                    message = "Mã giảm giá không hợp lệ.";
                    break;
            }

            // ⭐ LƯU TẤT CẢ THAY ĐỔI VÀO DATABASE
            _context.CartItems.UpdateRange(dbCartItems);
            
            // ⭐ LƯU VOUCHER STATE VÀO UserCartStates
            var cartState = await _context.UserCartStates.FirstOrDefaultAsync(s => s.UserId == userId);
            if (cartState == null)
            {
                cartState = new UserCartState
                {
                    UserId = userId,
                    PromotionCode = request.PromotionCode,
                    PromotionId = promo.Id,
                    DiscountAmount = cart.DiscountAmount ?? 0,
                    FreeShipping = cart.FreeShipping,
                    AppliedAt = DateTime.Now,
                    LastUpdated = DateTime.Now
                };
                _context.UserCartStates.Add(cartState);
            }
            else
            {
                cartState.PromotionCode = request.PromotionCode;
                cartState.PromotionId = promo.Id;
                cartState.DiscountAmount = cart.DiscountAmount ?? 0;
                cartState.FreeShipping = cart.FreeShipping;
                cartState.LastUpdated = DateTime.Now;
                _context.UserCartStates.Update(cartState);
            }
            
            await _context.SaveChangesAsync();

            if (string.IsNullOrEmpty(message) || message.Contains("không") || message.Contains("phải"))
            {
                return BadRequest(new { success = false, message = message });
            }

            // ⭐ TẢI LẠI TẤT CẢ ITEMS (bao gồm gift items vừa thêm)
            var allCartItems = await _context.CartItems
                .Include(c => c.Product)
                    .ThenInclude(p => p!.Images)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            // ⭐ Format response GIỐNG HỆT GetCart() để Flutter parse đúng
            var cartItemsResponse = allCartItems.Where(i => !i.IsGift).Select(item => new
            {
                productId = item.ProductId,
                quantity = item.Quantity,
                deliveryDate = item.DeliveryDate,
                deliveryTime = item.DeliveryTime,
                note = item.Note,
                isGift = item.IsGift,
                discount = item.Discount,
                product = item.Product == null ? null : new
                {
                    id = item.Product.Id,
                    name = item.Product.Name,
                    description = item.Product.Description,
                    price = item.Product.Price,
                    discountedPrice = item.Product.Price - (item.Discount ?? 0),
                    hasDiscount = item.Discount.HasValue && item.Discount.Value > 0,
                    imageUrl = item.Product.ImageUrl,
                    stockQuantity = item.Product.StockQuantity,
                    isActive = item.Product.IsActive,
                    images = item.Product.Images?.Select(img => new
                    {
                        id = img.Id,
                        url = img.Url,
                        productId = img.ProductId
                    }).ToList()
                }
            }).ToList();

            var giftItemsResponse = allCartItems.Where(i => i.IsGift).Select(item => new
            {
                productId = item.ProductId,
                quantity = item.Quantity,
                productName = item.Product?.Name ?? "",
                productImage = item.Product?.ImageUrl ?? "",
                originalPrice = item.Product?.Price ?? 0,
                discount = item.Discount ?? 0,
                finalPrice = (item.Product?.Price ?? 0) - (item.Discount ?? 0),
                isFree = item.Discount >= (item.Product?.Price ?? 0)
            }).ToList();

            var responseCartCount = allCartItems.Where(i => !i.IsGift).Sum(i => i.Quantity);
            var responseTotal = allCartItems.Where(i => !i.IsGift).Sum(i => (i.Product?.Price ?? 0) * i.Quantity);

            return Ok(new
            {
                success = true,
                message = message,
                cart = new
                {
                    cartItems = cartItemsResponse,
                    promotionCode = request.PromotionCode,
                    discountAmount = cart.DiscountAmount,
                    freeShipping = cart.FreeShipping
                },
                giftItems = giftItemsResponse,
                hasGiftItems = giftItemsResponse.Any(),
                cartCount = responseCartCount,
                total = responseTotal
            });
        }

        // DELETE: api/ShoppingCartApi/remove
        [HttpDelete("remove")]
        public async Task<IActionResult> RemoveFromCart([FromQuery] int productId, [FromQuery] DateTime? deliveryDate, [FromQuery] string? deliveryTime)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
            }

            // ⭐ XÓA KHỎI DATABASE
            var itemToRemove = await _context.CartItems
                .FirstOrDefaultAsync(i => i.UserId == userId && 
                                          i.ProductId == productId && 
                                          i.DeliveryDate == deliveryDate && 
                                          i.DeliveryTime == deliveryTime);

            if (itemToRemove != null)
            {
                _context.CartItems.Remove(itemToRemove);
                
                // ⭐ XÓA GIFT ITEMS nếu có (tạm thời xóa hết, sau này có thể validate voucher)
                var giftItems = await _context.CartItems
                    .Where(i => i.UserId == userId && i.IsGift)
                    .ToListAsync();
                if (giftItems.Any())
                {
                    _context.CartItems.RemoveRange(giftItems);
                }
                
                await _context.SaveChangesAsync();
            }
            
            var cartCount = await _context.CartItems
                .Where(i => i.UserId == userId && !i.IsGift)
                .SumAsync(i => i.Quantity);
                
            return Ok(new { success = true, message = "Đã xóa sản phẩm khỏi giỏ hàng!", cartCount = cartCount });
        }

        // PUT: api/ShoppingCartApi/increase
        [HttpPut("increase")]
        public async Task<IActionResult> IncreaseQuantity([FromBody] UpdateQuantityRequest request)
        {
            if (request == null || request.ProductId <= 0)
            {
                return BadRequest(new { success = false, message = "Thông tin không hợp lệ!" });
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
            }

            // ⭐ TĂNG SỐ LƯỢNG TRONG DATABASE
            var item = await _context.CartItems
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.UserId == userId && i.ProductId == request.ProductId && !i.IsGift);
                
            if (item != null)
            {
                // Kiểm tra tồn kho
                if (item.Product != null && item.Quantity + 1 > item.Product.StockQuantity)
                {
                    return BadRequest(new { success = false, message = $"Sản phẩm chỉ còn {item.Product.StockQuantity} sản phẩm" });
                }
                
                item.Quantity++;
                _context.CartItems.Update(item);
                await _context.SaveChangesAsync();
            }
            
            var cartCount = await _context.CartItems
                .Where(i => i.UserId == userId && !i.IsGift)
                .SumAsync(i => i.Quantity);
                
            var total = await _context.CartItems
                .Where(i => i.UserId == userId)
                .Include(i => i.Product)
                .SumAsync(i => i.Product!.Price * i.Quantity);
            
            return Ok(new { success = true, cartCount = cartCount, newTotal = total });
        }

        // PUT: api/ShoppingCartApi/decrease
        [HttpPut("decrease")]
        public async Task<IActionResult> DecreaseQuantity([FromBody] UpdateQuantityRequest request)
        {
            if (request == null || request.ProductId <= 0)
            {
                return BadRequest(new { success = false, message = "Thông tin không hợp lệ!" });
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
            }

            // ⭐ GIẢM SỐ LƯỢNG TRONG DATABASE
            var item = await _context.CartItems
                .FirstOrDefaultAsync(i => i.UserId == userId && i.ProductId == request.ProductId && !i.IsGift);
                
            if (item != null)
            {
                if (item.Quantity > 1)
                {
                    item.Quantity--;
                    _context.CartItems.Update(item);
                }
                else
                {
                    _context.CartItems.Remove(item);
                    
                    // ⭐ XÓA GIFT ITEMS nếu giỏ hàng trống
                    var remainingItems = await _context.CartItems
                        .Where(i => i.UserId == userId && !i.IsGift)
                        .CountAsync();
                    if (remainingItems == 0)
                    {
                        var giftItems = await _context.CartItems
                            .Where(i => i.UserId == userId && i.IsGift)
                            .ToListAsync();
                        _context.CartItems.RemoveRange(giftItems);
                    }
                }
                
                await _context.SaveChangesAsync();
            }
            
            var cartCount = await _context.CartItems
                .Where(i => i.UserId == userId && !i.IsGift)
                .SumAsync(i => i.Quantity);
                
            var total = await _context.CartItems
                .Where(i => i.UserId == userId)
                .Include(i => i.Product)
                .SumAsync(i => i.Product!.Price * i.Quantity);
            
            return Ok(new { success = true, cartCount = cartCount, newTotal = total });
        }

        // PUT: api/ShoppingCartApi/update-quantity
        [HttpPut("update-quantity")]
        public async Task<IActionResult> UpdateQuantity([FromBody] UpdateQuantityRequest request)
        {
            if (request == null || request.ProductId <= 0)
            {
                return BadRequest(new { success = false, message = "Thông tin không hợp lệ!" });
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
            }

            var item = await _context.CartItems
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.UserId == userId && i.ProductId == request.ProductId && !i.IsGift);
                
            if (item == null)
            {
                return NotFound(new { success = false, message = "Sản phẩm không tồn tại trong giỏ hàng!" });
            }
            
            if (request.Quantity < 1)
            {
                request.Quantity = 1;
            }
            
            item.Quantity = request.Quantity;
            _context.CartItems.Update(item);
            await _context.SaveChangesAsync();
            
            var newTotal = await _context.CartItems
                .Where(i => i.UserId == userId)
                .Include(i => i.Product)
                .SumAsync(i => i.Product!.Price * i.Quantity);
                
            return Ok(new { success = true, newTotal = newTotal });
        }

        // PUT: api/ShoppingCartApi/update-delivery
        [HttpPut("update-delivery")]
        public async Task<IActionResult> UpdateDeliveryInfo([FromBody] UpdateDeliveryRequest request)
        {
            if (request == null || request.ProductId <= 0)
            {
                return BadRequest(new { success = false, message = "Thông tin không hợp lệ!" });
            }

            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var item = await _context.CartItems
                    .FirstOrDefaultAsync(i => i.UserId == userId && i.ProductId == request.ProductId && !i.IsGift);
                    
                if (item != null)
                {
                    if (!string.IsNullOrEmpty(request.DeliveryDate))
                    {
                        if (DateTime.TryParseExact(request.DeliveryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            item.DeliveryDate = parsedDate;
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(request.DeliveryTime))
                    {
                        item.DeliveryTime = request.DeliveryTime;
                    }
                    
                    _context.CartItems.Update(item);
                    await _context.SaveChangesAsync();
                    return Ok(new { success = true, message = "Cập nhật thành công" });
                }
                
                return NotFound(new { success = false, message = "Không tìm thấy sản phẩm trong giỏ hàng" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // GET: api/ShoppingCartApi/check
        [HttpGet("check")]
        public async Task<IActionResult> CheckCart()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Ok(new { hasItems = false, cartCount = 0 });
            }

            var cartCount = await _context.CartItems
                .Where(i => i.UserId == userId && !i.IsGift)
                .SumAsync(i => i.Quantity);
                
            bool hasItems = cartCount > 0;
            
            return Ok(new { hasItems = hasItems, cartCount = cartCount });
        }

        // POST: api/ShoppingCartApi/remove-promotion
        [HttpPost("remove-promotion")]
        public async Task<IActionResult> RemovePromotionCode()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
            }

            // ⭐ XÓA TẤT CẢ GIFT ITEMS VÀ DISCOUNT KHỎI DATABASE
            var allItems = await _context.CartItems
                .Where(i => i.UserId == userId)
                .ToListAsync();

            // Xóa gift items
            var giftItems = allItems.Where(i => i.IsGift).ToList();
            if (giftItems.Any())
            {
                _context.CartItems.RemoveRange(giftItems);
            }

            // Reset discount của các items thường
            var regularItems = allItems.Where(i => !i.IsGift).ToList();
            foreach (var item in regularItems)
            {
                item.Discount = 0;
            }
            _context.CartItems.UpdateRange(regularItems);
            
            // ⭐ XÓA VOUCHER STATE
            var cartState = await _context.UserCartStates.FirstOrDefaultAsync(s => s.UserId == userId);
            if (cartState != null)
            {
                cartState.PromotionCode = null;
                cartState.PromotionId = null;
                cartState.DiscountAmount = 0;
                cartState.FreeShipping = false;
                cartState.LastUpdated = DateTime.Now;
                _context.UserCartStates.Update(cartState);
            }
            
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đã bỏ chọn voucher" });
        }

        // ⭐ HELPER: Validate voucher với code và cart items
        private async Task<bool> ValidatePromotionCode(string promotionCode, List<CartItem> cartItems)
        {
            if (string.IsNullOrEmpty(promotionCode))
                return false;
                
            var promoCode = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .FirstOrDefaultAsync(pc => pc.Code == promotionCode && pc.IsActive);
                
            if (promoCode == null)
                return false;
                
            var promo = promoCode.Promotion;
            if (promo == null || !promo.IsActive)
                return false;
                
            var now = DateTime.Now;
            if (promo.StartDate > now || (promo.EndDate.HasValue && promo.EndDate.Value < now))
                return false;
            
            // Tính total sau ProductDiscount
            decimal total = cartItems.Where(i => !i.IsGift)
                .Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity);
            
            // Kiểm tra MinOrderValue
            if (promo.MinOrderValue.HasValue && total < promo.MinOrderValue.Value)
                return false;
                
            // Kiểm tra MinProductQuantity
            if (promo.MinProductQuantity.HasValue)
            {
                int totalQty = cartItems.Where(i => !i.IsGift).Sum(i => i.Quantity);
                if (totalQty < promo.MinProductQuantity.Value)
                    return false;
            }
            
            return true;
        }

        // ⭐ THÊM METHOD MỚI: Kiểm tra voucher còn hợp lệ không (old version - giữ lại cho tương thích)
        private async Task<bool> ValidatePromotionCode(ShoppingCart cart)
        {
            if (string.IsNullOrEmpty(cart.PromotionCode))
                return false;
                
            var promoCode = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                    .ThenInclude(p => p!.PromotionGifts)
                .FirstOrDefaultAsync(pc => pc.Code == cart.PromotionCode && pc.IsActive);
                
            if (promoCode == null)
                return false;
                
            var promo = promoCode.Promotion;
            if (promo == null)
                return false;
                
            var now = DateTime.Now;
            
            // Tính total sau ProductDiscount
            decimal total = 0;
            if (cart.CartItems != null)
            {
                foreach (var item in cart.CartItems.Where(i => !i.IsGift))
                {
                    var priceAfterDiscount = (item.Product?.Price ?? 0) - (item.Discount ?? 0);
                    total += priceAfterDiscount * item.Quantity;
                }
            }
            
            // Kiểm tra MinOrderValue
            if (promo.MinOrderValue.HasValue && total < promo.MinOrderValue.Value)
                return false;
                
            // Kiểm tra MinProductQuantity
            if (promo.MinProductQuantity.HasValue && cart.CartItems != null)
            {
                int totalQty = cart.CartItems.Where(i => !i.IsGift).Sum(i => i.Quantity);
                if (totalQty < promo.MinProductQuantity.Value)
                    return false;
            }
            
            // Kiểm tra điều kiện Gift (Mua X tặng Y)
            if (promo.Type == PromotionType.Gift && cart.CartItems != null)
            {
                var giftEntity = promo.PromotionGifts?.FirstOrDefault();
                if (giftEntity != null)
                {
                    var buyProductIds = await _context.PromotionGiftBuyProducts
                        .Where(x => x.PromotionGiftId == giftEntity.Id)
                        .Select(x => x.ProductId)
                        .ToListAsync();
                        
                    var buyCategoryIds = await _context.PromotionGiftBuyCategories
                        .Where(x => x.PromotionGiftId == giftEntity.Id)
                        .Select(x => x.CategoryId)
                        .ToListAsync();
                        
                    int totalBuyQty = cart.CartItems
                        .Where(i => !i.IsGift && (buyProductIds.Contains(i.ProductId) ||
                            (i.Product != null && i.Product.ProductCategories != null &&
                                i.Product.ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))))
                        .Sum(i => i.Quantity);
                        
                    // Kiểm tra số lượng tối thiểu theo BuyConditionType
                    if (giftEntity.BuyConditionType == "MinQuantity" && giftEntity.BuyConditionValue.HasValue)
                    {
                        if (totalBuyQty < giftEntity.BuyConditionValue.Value)
                            return false;
                    }
                    else if (giftEntity.BuyQuantity > 0 && totalBuyQty < giftEntity.BuyQuantity)
                    {
                        return false;
                    }
                        
                    // Kiểm tra MinValue
                    if (giftEntity.BuyConditionType == "MinValue" && giftEntity.BuyConditionValueMoney.HasValue)
                    {
                        decimal totalBuyValue = cart.CartItems
                            .Where(i => !i.IsGift && (buyProductIds.Contains(i.ProductId) ||
                                (i.Product != null && i.Product.ProductCategories != null &&
                                    i.Product.ProductCategories.Any(pc => buyCategoryIds.Contains(pc.CategoryId)))))
                            .Sum(i => ((i.Product?.Price ?? 0) - (i.Discount ?? 0)) * i.Quantity);
                            
                        if (totalBuyValue < giftEntity.BuyConditionValueMoney.Value)
                            return false;
                    }
                }
            }
            
            return true;
        }
    }

    // Request models
    public class AddToCartRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
        public string? DeliveryDate { get; set; }
        public string? DeliveryTime { get; set; }
        public decimal? DiscountedPrice { get; set; }
    }

    public class ApplyPromotionRequest
    {
        public string PromotionCode { get; set; } = string.Empty;
    }

    public class UpdateQuantityRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class UpdateDeliveryRequest
    {
        public int ProductId { get; set; }
        public string DeliveryDate { get; set; } = string.Empty;
        public string DeliveryTime { get; set; } = string.Empty;
    }
}
