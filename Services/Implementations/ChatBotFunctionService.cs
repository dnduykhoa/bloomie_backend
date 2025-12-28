using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using Bloomie.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Bloomie.Extensions;

namespace Bloomie.Services.Implementations
{
    public class ChatBotFunctionService : IChatBotFunctionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ChatBotFunctionService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private const decimal FREE_SHIPPING_THRESHOLD = 500000m;
        private const decimal SHIPPING_FEE = 30000m;

        public ChatBotFunctionService(
            ApplicationDbContext context,
            ILogger<ChatBotFunctionService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        private string GetCartKey(string userId)
        {
            return $"Cart_{userId}";
        }

        private ShoppingCart GetCart(string userId)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            
            // Try Session first (for web requests)
            if (session != null)
            {
                var cartKey = GetCartKey(userId);
                var sessionCart = session.GetObjectFromJson<ShoppingCart>(cartKey);
                if (sessionCart != null) return sessionCart;
            }
            
            // Fallback to database (for API requests without Session)
            _logger.LogInformation("[ChatBot] Session unavailable, loading cart from database for user {UserId}", userId);
            var cart = new ShoppingCart();
            var dbCartItems = _context.CartItems
                .Include(c => c.Product)
                .Where(c => c.UserId == userId)
                .ToList();
            
            // Break circular reference: set Product.Images to null
            foreach (var item in dbCartItems)
            {
                if (item.Product != null)
                {
                    item.Product.Images = null;
                }
            }
            
            cart.CartItems = dbCartItems;
            return cart;
        }

        private void SaveCart(string userId, ShoppingCart cart)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            
            // Clean up navigation properties to avoid circular references
            foreach (var item in cart.CartItems)
            {
                if (item.Product != null)
                {
                    item.Product.Images = null;
                    item.Product = null;
                }
            }
            
            // Save to Session if available (web requests)
            if (session != null)
            {
                var cartKey = GetCartKey(userId);
                session.SetObjectAsJson(cartKey, cart);
                _logger.LogInformation("[ChatBot] Cart saved to Session for user {UserId}", userId);
            }
            else
            {
                // Save to database if Session unavailable (API requests)
                _logger.LogInformation("[ChatBot] Session unavailable, saving cart to database for user {UserId}", userId);
                
                // Remove old cart items
                var oldItems = _context.CartItems.Where(c => c.UserId == userId).ToList();
                _context.CartItems.RemoveRange(oldItems);
                
                // Add new cart items
                foreach (var item in cart.CartItems)
                {
                    item.UserId = userId;
                    _context.CartItems.Add(item);
                }
                
                _context.SaveChanges();
            }
        }

        public async Task<FunctionCallResult> AddToCartAsync(string userId, AddToCartParams parameters)
        {
            try
            {
                _logger.LogInformation($"[ChatBot] Adding product '{parameters.ProductName}' to cart for user {userId}");

                // Find product by name (flexible search: case-insensitive and partial match)
                var searchName = parameters.ProductName.Trim().ToLower();
                var product = await _context.Products
                    .Include(p => p.Images)
                    .Where(p => p.IsActive && p.Name.ToLower().Contains(searchName))
                    .FirstOrDefaultAsync();

                if (product == null)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = "Sản phẩm không tồn tại hoặc không còn kinh doanh."
                    };
                }

                if (product.StockQuantity < parameters.Quantity)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = $"Sản phẩm '{product.Name}' chỉ còn {product.StockQuantity} sản phẩm trong kho."
                    };
                }

                // Lấy giỏ hàng từ Session
                var cart = GetCart(userId);
                if (cart.CartItems == null)
                    cart.CartItems = new List<CartItem>();

                // Kiểm tra sản phẩm đã có trong giỏ chưa
                var existingItem = cart.CartItems.FirstOrDefault(i => i.ProductId == product.Id && !i.IsGift);
                
                int totalQuantity;
                if (existingItem != null)
                {
                    existingItem.Quantity += parameters.Quantity;
                    totalQuantity = existingItem.Quantity;
                }
                else
                {
                    var cartItem = new CartItem
                    {
                        ProductId = product.Id,
                        // DON'T save Product navigation - causes circular reference!
                        Product = null,
                        Quantity = parameters.Quantity,
                        IsGift = false,
                        DeliveryDate = DateTime.Now.AddDays(1).Date,
                        DeliveryTime = "08:00 - 10:00"
                    };
                    cart.CartItems.Add(cartItem);
                    totalQuantity = parameters.Quantity;
                }

                // Lưu giỏ hàng vào Session
                SaveCart(userId, cart);

                return new FunctionCallResult
                {
                    Success = true,
                    Message = $"✅ **Đã thêm vào giỏ hàng**\n\n" +
                              $"🌸 **Sản phẩm:** {product.Name}\n" +
                              $"🔢 **Số lượng:** {parameters.Quantity}\n" +
                              $"💰 **Đơn giá:** {product.Price:N0}đ\n" +
                              $"📦 **Tổng trong giỏ:** {totalQuantity} sản phẩm",
                    CartCount = cart.CartItems.Where(i => !i.IsGift).Sum(i => i.Quantity),
                    Data = new
                    {
                        productId = product.Id,
                        productName = product.Name,
                        quantity = parameters.Quantity,
                        totalQuantity = totalQuantity,
                        price = product.Price,
                        subtotal = product.Price * totalQuantity
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBot] Error adding to cart");
                return new FunctionCallResult
                {
                    Success = false,
                    Message = "❌ Có lỗi xảy ra khi thêm sản phẩm vào giỏ hàng."
                };
            }
        }

        public async Task<FunctionCallResult> RemoveFromCartAsync(string userId, RemoveFromCartParams parameters)
        {
            try
            {
                // Tìm sản phẩm theo tên (case-insensitive, flexible matching)
                var productNameLower = parameters.ProductName.Trim().ToLower();
                
                // Lấy giỏ hàng từ Session
                var cart = GetCart(userId);

                if (cart.CartItems == null || !cart.CartItems.Any())
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = "Giỏ hàng trống."
                    };
                }

                // Tìm sản phẩm trong database để có tên sản phẩm
                var product = await _context.Products
                    .FirstOrDefaultAsync(p => p.Name.ToLower().Contains(productNameLower));

                if (product == null)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = $"Sản phẩm '{parameters.ProductName}' không tìm thấy."
                    };
                }

                // Xóa sản phẩm khỏi giỏ hàng Session
                var cartItem = cart.CartItems.FirstOrDefault(i => i.ProductId == product.Id);

                if (cartItem == null)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = $"Sản phẩm '{parameters.ProductName}' không có trong giỏ hàng."
                    };
                }

                var productName = product.Name;
                cart.CartItems.Remove(cartItem);
                SaveCart(userId, cart);

                return new FunctionCallResult
                {
                    Success = true,
                    Message = $"✅ Đã xóa '{productName}' khỏi giỏ hàng."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBot] Error removing from cart");
                return new FunctionCallResult
                {
                    Success = false,
                    Message = "❌ Có lỗi xảy ra khi xóa sản phẩm."
                };
            }
        }

        public async Task<FunctionCallResult> GetCartSummaryAsync(string userId)
        {
            try
            {
                // Lấy giỏ hàng từ Session
                var cart = GetCart(userId);

                if (cart.CartItems == null || !cart.CartItems.Any(i => !i.IsGift))
                {
                    return new FunctionCallResult
                    {
                        Success = true,
                        Message = "🛒 Giỏ hàng trống.",
                        Data = new CartSummaryData
                        {
                            TotalItems = 0,
                            Subtotal = 0,
                            Discount = 0,
                            ShippingFee = 0,
                            Total = 0,
                            Items = new List<CartItemSummary>()
                        }
                    };
                }

                // Load product data và tính giá sau discount
                var nonGiftItems = cart.CartItems.Where(i => !i.IsGift).ToList();
                var productIds = nonGiftItems.Select(i => i.ProductId).Distinct().ToList();
                
                _logger.LogInformation($"[ChatBot] Loading {productIds.Count} products: {string.Join(", ", productIds)}");
                
                var products = await _context.Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id);
                
                // Load active discounts
                var now = DateTime.Now;
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .ToListAsync();
                
                _logger.LogInformation($"[ChatBot] Loaded {products.Count} products and {activeDiscounts.Count} active discounts");

                var items = nonGiftItems.Select(i =>
                {
                    var product = i.Product ?? (products.ContainsKey(i.ProductId) ? products[i.ProductId] : null);
                    
                    if (product == null)
                    {
                        _logger.LogWarning($"[ChatBot] Product not found for ProductId={i.ProductId}");
                        return new CartItemSummary
                        {
                            ProductId = i.ProductId,
                            ProductName = "N/A",
                            Quantity = i.Quantity,
                            Price = 0,
                            Subtotal = 0
                        };
                    }
                    
                    // Calculate discounted price (same logic as web)
                    decimal finalPrice = product.Price;
                    foreach (var discount in activeDiscounts)
                    {
                        bool isApplicable = false;
                        
                        if (discount.ApplyTo == "all")
                        {
                            isApplicable = true;
                        }
                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                        {
                            try
                            {
                                var productIdList = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                                isApplicable = productIdList?.Contains(product.Id) ?? false;
                            }
                            catch { }
                        }
                        
                        if (isApplicable)
                        {
                            if (discount.DiscountType == "Percentage" || discount.DiscountType == "percent")
                            {
                                var discountAmount = product.Price * (discount.DiscountValue / 100);
                                if (discount.MaxDiscount.HasValue && discountAmount > discount.MaxDiscount.Value)
                                {
                                    discountAmount = discount.MaxDiscount.Value;
                                }
                                finalPrice = product.Price - discountAmount;
                            }
                            else if (discount.DiscountType == "FixedAmount" || discount.DiscountType == "fixed_amount")
                            {
                                finalPrice = product.Price - discount.DiscountValue;
                            }
                            break; // Use first matching discount
                        }
                    }
                    
                    _logger.LogInformation($"[ChatBot] Product: {product.Name}, Original: {product.Price}, Final: {finalPrice}");
                    
                    return new CartItemSummary
                    {
                        ProductId = i.ProductId,
                        ProductName = product.Name,
                        Quantity = i.Quantity,
                        Price = finalPrice,
                        Subtotal = finalPrice * i.Quantity
                    };
                }).ToList();

                var subtotal = items.Sum(i => i.Subtotal);
                var discount = cart.DiscountAmount ?? 0;
                var shippingFee = cart.FreeShipping || subtotal >= FREE_SHIPPING_THRESHOLD ? 0 : SHIPPING_FEE;

                var summary = new CartSummaryData
                {
                    TotalItems = items.Sum(i => i.Quantity),
                    Subtotal = subtotal,
                    Discount = discount,
                    ShippingFee = shippingFee,
                    Total = subtotal - discount + shippingFee,
                    Items = items
                };

                // Tạo message chi tiết
                var messageBuilder = new System.Text.StringBuilder();
                messageBuilder.AppendLine($"🛒 **Giỏ hàng của bạn** ({items.Sum(i => i.Quantity)} sản phẩm)");
                messageBuilder.AppendLine();
                
                foreach (var item in items)
                {
                    messageBuilder.AppendLine($"🌸 **{item.ProductName}**");
                    messageBuilder.AppendLine($"   • Số lượng: {item.Quantity}");
                    messageBuilder.AppendLine($"   • Đơn giá: {item.Price:#,##0}đ");
                    messageBuilder.AppendLine($"   • Tạm tính: {item.Subtotal:#,##0}đ");
                    messageBuilder.AppendLine();
                }
                
                messageBuilder.AppendLine($"💰 **Chi tiết thanh toán**");
                messageBuilder.AppendLine($"📊 Tạm tính: {subtotal:#,##0}đ");
                
                if (discount > 0)
                {
                    messageBuilder.AppendLine($"🎁 Giảm giá: -{discount:#,##0}đ");
                }
                
                messageBuilder.AppendLine($"🚚 Phí vận chuyển: {shippingFee:#,##0}đ");
                
                if (subtotal >= FREE_SHIPPING_THRESHOLD)
                {
                    messageBuilder.AppendLine($"  ✅ Miễn phí ship cho đơn ≥ {FREE_SHIPPING_THRESHOLD:#,##0}đ");
                }
                
                messageBuilder.AppendLine();
                messageBuilder.AppendLine($"💰 **Tổng cộng: {summary.Total:#,##0}đ**");

                return new FunctionCallResult
                {
                    Success = true,
                    Message = messageBuilder.ToString(),
                    Data = summary
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBot] Error getting cart summary");
                return new FunctionCallResult
                {
                    Success = false,
                    Message = "❌ Có lỗi xảy ra khi lấy thông tin giỏ hàng."
                };
            }
        }

        public async Task<FunctionCallResult> CreateOrderAsync(string userId, CreateOrderParams parameters)
        {
            try
            {
                _logger.LogInformation($"[ChatBot] Creating order for user {userId}");

                // Get cart from Session
                var cart = GetCart(userId);

                if (cart.CartItems == null || !cart.CartItems.Any(i => !i.IsGift))
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = "❌ Giỏ hàng trống. Vui lòng thêm sản phẩm trước khi đặt hàng."
                    };
                }

                // Load products for cart items
                var nonGiftItems = cart.CartItems.Where(i => !i.IsGift).ToList();
                var productIds = nonGiftItems.Select(i => i.ProductId).Distinct().ToList();
                var products = await _context.Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id);

                // Attach product references
                foreach (var item in cart.CartItems)
                {
                    if (products.ContainsKey(item.ProductId))
                    {
                        item.Product = products[item.ProductId];
                    }
                }

                // Get user info
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = "❌ Không tìm thấy thông tin người dùng."
                    };
                }

                // Use provided info or defaults
                var shippingAddress = parameters.ShippingAddress;
                var phoneNumber = parameters.PhoneNumber ?? user.PhoneNumber;

                if (string.IsNullOrEmpty(shippingAddress))
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = "❌ Vui lòng cung cấp địa chỉ giao hàng.\nVí dụ: 'Đặt hàng giao đến 123 Nguyễn Huệ, Quận 1, TP.HCM'"
                    };
                }

                if (string.IsNullOrEmpty(phoneNumber))
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = "❌ Vui lòng cung cấp số điện thoại.\nVí dụ: 'Số điện thoại: 0909123456'"
                    };
                }

                // Calculate totals
                var subtotal = cart.CartItems.Where(i => !i.IsGift).Sum(i => (i.Product?.Price ?? 0) * i.Quantity);
                var discount = cart.DiscountAmount ?? 0;
                var shippingFee = cart.FreeShipping || subtotal >= FREE_SHIPPING_THRESHOLD ? 0 : SHIPPING_FEE;
                var total = subtotal - discount + shippingFee;

                // Create order
                var order = new Order
                {
                    UserId = userId,
                    OrderDate = DateTime.Now,
                    TotalAmount = total,
                    Status = "Pending",
                    PaymentMethod = parameters.PaymentMethod,
                    PaymentStatus = "Pending",
                    ShippingAddress = shippingAddress,
                    Phone = phoneNumber,
                    ReceiverName = user.FullName,
                    Note = parameters.Notes,
                    ShippingFee = shippingFee,
                    VoucherDiscount = discount,
                    OrderDetails = new List<OrderDetail>()
                };

                // Add order details
                foreach (var cartItem in cart.CartItems)
                {
                    if (cartItem.Product == null) continue;

                    order.OrderDetails.Add(new OrderDetail
                    {
                        ProductId = cartItem.ProductId,
                        Quantity = cartItem.Quantity,
                        UnitPrice = cartItem.Product.Price,
                        IsGift = cartItem.IsGift,
                        DeliveryDate = cartItem.DeliveryDate,
                        DeliveryTime = cartItem.DeliveryTime,
                        Note = cartItem.Note
                    });

                    // Update stock
                    cartItem.Product.StockQuantity -= cartItem.Quantity;
                }

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                // Clear cart from Session
                cart.CartItems.Clear();
                cart.DiscountAmount = null;
                cart.PromotionCode = null;
                cart.FreeShipping = false;
                SaveCart(userId, cart);

                return new FunctionCallResult
                {
                    Success = true,
                    Message = $"🎉 **Đặt hàng thành công!**\n\n" +
                              $"📦 **Mã đơn hàng:** #{order.Id}\n" +
                              $"💰 **Tổng tiền:** {total:#,##0}đ\n" +
                              $"💳 **Thanh toán:** {parameters.PaymentMethod}\n" +
                              $"🚚 **Giao đến:** {shippingAddress}\n" +
                              $"⏰ **Dự kiến giao:** 2-4 giờ (nội thành)\n\n" +
                              $"✅ Đơn hàng đang được xử lý. Chúng tôi sẽ liên hệ bạn sớm nhất!",
                    Data = new
                    {
                        orderId = order.Id,
                        totalAmount = total,
                        paymentMethod = parameters.PaymentMethod,
                        shippingAddress = shippingAddress,
                        estimatedDelivery = "2-4 giờ (nội thành)"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBot] Error creating order");
                return new FunctionCallResult
                {
                    Success = false,
                    Message = "❌ Có lỗi xảy ra khi đặt hàng. Vui lòng thử lại."
                };
            }
        }

        public async Task<FunctionCallResult> ApplyVoucherAsync(string userId, ApplyVoucherParams parameters)
        {
            try
            {
                // Find voucher
                var voucher = await _context.UserVouchers
                    .Include(v => v.PromotionCode)
                    .FirstOrDefaultAsync(v =>
                        v.UserId == userId &&
                        v.PromotionCode!.Code == parameters.VoucherCode &&
                        !v.IsUsed &&
                        v.ExpiryDate >= DateTime.Now);

                if (voucher?.PromotionCode == null)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = $"❌ Mã voucher '{parameters.VoucherCode}' không hợp lệ hoặc đã hết hạn."
                    };
                }

                // Get cart summary
                var cartSummary = await GetCartSummaryAsync(userId);
                if (!cartSummary.Success || cartSummary.Data == null)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = "❌ Giỏ hàng trống, không thể áp dụng voucher."
                    };
                }

                return new FunctionCallResult
                {
                    Success = true,
                    Message = $"✅ Mã voucher '{parameters.VoucherCode}' hợp lệ!",
                    Data = new
                    {
                        voucherCode = parameters.VoucherCode,
                        message = "Voucher sẽ được áp dụng khi đặt hàng"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBot] Error applying voucher");
                return new FunctionCallResult
                {
                    Success = false,
                    Message = "❌ Có lỗi xảy ra khi kiểm tra voucher."
                };
            }
        }

        public async Task<FunctionCallResult> GetUserInfoAsync(string userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = "❌ Không tìm thấy thông tin người dùng."
                    };
                }

                // Lấy voucher khả dụng
                var now = DateTime.Now;
                var availableVouchers = await _context.UserVouchers
                    .Include(v => v.PromotionCode)
                        .ThenInclude(pc => pc!.Promotion)
                            .ThenInclude(p => p!.PromotionGifts)
                    .Where(v => v.UserId == userId && 
                                !v.IsUsed && 
                                v.ExpiryDate >= now &&
                                v.PromotionCode != null)
                    .Select(v => new
                    {
                        code = v.PromotionCode!.Code,
                        isPercent = v.PromotionCode.IsPercent,
                        value = v.PromotionCode.Value,
                        minOrderValue = v.PromotionCode.MinOrderValue,
                        maxDiscount = v.PromotionCode.MaxDiscount,
                        expiryDate = v.ExpiryDate,
                        promotionName = v.PromotionCode.Promotion != null ? v.PromotionCode.Promotion.Name : null,
                        isGiftVoucher = v.PromotionCode.Promotion != null && 
                                       v.PromotionCode.Promotion.PromotionGifts != null && 
                                       v.PromotionCode.Promotion.PromotionGifts.Any()
                    })
                    .ToListAsync();

                var voucherList = availableVouchers.Select(v => new
                {
                    code = v.code,
                    isPercent = v.isPercent,
                    value = v.value,
                    minOrderValue = v.minOrderValue,
                    maxDiscount = v.maxDiscount,
                    expiryDate = v.expiryDate,
                    isGiftVoucher = v.isGiftVoucher,
                    promotionName = v.promotionName
                }).ToList();

                if (!voucherList.Any())
                {
                    return new FunctionCallResult
                    {
                        Success = true,
                        Message = "Bạn chưa có voucher nào. Shop sẽ có nhiều chương trình khuyến mãi hấp dẫn, bạn nhớ theo dõi nhé! 🎁",
                        Data = new { vouchers = new List<object>() }
                    };
                }

                return new FunctionCallResult
                {
                    Success = true,
                    Message = $"Bạn có {voucherList.Count} voucher đang chờ được dùng:",
                    Data = new { vouchers = voucherList }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBot] Error getting user info");
                return new FunctionCallResult
                {
                    Success = false,
                    Message = "❌ Có lỗi xảy ra."
                };
            }
        }

        public async Task<FunctionCallResult> GetOrderStatusAsync(string userId, GetOrderStatusParams parameters)
        {
            try
            {
                _logger.LogInformation("[ChatBot] Getting order status for OrderId={OrderId}, UserId={UserId}", parameters.OrderId, userId);

                var order = await _context.Orders
                    .Include(o => o.OrderDetails)
                        .ThenInclude(od => od.Product)
                    .FirstOrDefaultAsync(o => o.OrderId == parameters.OrderId && o.UserId == userId);

                if (order == null)
                {
                    return new FunctionCallResult
                    {
                        Success = false,
                        Message = $"❌ Không tìm thấy đơn hàng '{parameters.OrderId}' hoặc đơn hàng không thuộc về bạn."
                    };
                }

                // Map order status to Vietnamese
                var statusText = order.Status switch
                {
                    "Pending" => "Chờ xác nhận",
                    "Confirmed" => "Đã xác nhận",
                    "Preparing" => "Đang chuẩn bị",
                    "Shipping" => "Đang giao hàng",
                    "Delivered" => "Đã giao",
                    "Cancelled" => "Đã hủy",
                    "Returned" => "Đã trả hàng",
                    _ => order.Status
                };

                var orderData = new OrderStatusData
                {
                    OrderId = order.OrderId,
                    OrderDate = order.OrderDate,
                    Status = order.Status,
                    StatusText = statusText,
                    TotalAmount = order.TotalAmount,
                    ShippingAddress = order.ShippingAddress,
                    PhoneNumber = order.Phone,
                    PaymentMethod = order.PaymentMethod,
                    Items = order.OrderDetails.Select(od => new OrderItemSummary
                    {
                        ProductName = od.Product?.Name ?? "Unknown",
                        Quantity = od.Quantity,
                        Price = od.UnitPrice,
                        Subtotal = od.Quantity * od.UnitPrice
                    }).ToList(),
                    TrackingInfo = GetTrackingInfo(order.Status)
                };

                var itemsList = string.Join(", ", orderData.Items.Select(i => $"{i.ProductName} x{i.Quantity}"));
                
                // Map payment method to Vietnamese
                var paymentMethodText = order.PaymentMethod switch
                {
                    "COD" => "Thanh toán khi nhận hàng (COD)",
                    "VNPAY" => "Thanh toán online qua VNPAY",
                    "BankTransfer" => "Chuyển khoản ngân hàng",
                    _ => order.PaymentMethod ?? "Chưa xác định"
                };
                
                // Check payment status
                var paymentStatus = order.PaymentMethod == "COD" 
                    ? "⏳ Chưa thanh toán (thanh toán khi nhận hàng)" 
                    : "✅ Đã thanh toán";
                
                return new FunctionCallResult
                {
                    Success = true,
                    Message = $"📦 **Thông tin đơn hàng #{order.OrderId}**\n\n" +
                              $"📅 **Ngày đặt:** {order.OrderDate:dd/MM/yyyy HH:mm}\n" +
                              $"📍 **Trạng thái:** {statusText}\n" +
                              $"💰 **Tổng tiền:** {order.TotalAmount:N0}đ\n" +
                              $"💳 **Thanh toán:** {paymentMethodText}\n" +
                              $"💵 **Trạng thái TT:** {paymentStatus}\n\n" +
                              $"📦 **Sản phẩm:** {itemsList}\n\n" +
                              $"🚚 {orderData.TrackingInfo}",
                    Data = orderData
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBot] Error getting order status for OrderId={OrderId}", parameters.OrderId);
                return new FunctionCallResult
                {
                    Success = false,
                    Message = "❌ Có lỗi xảy ra khi kiểm tra trạng thái đơn hàng."
                };
            }
        }

        private string GetTrackingInfo(string status)
        {
            return status switch
            {
                "Pending" => "Đơn hàng đang chờ xác nhận từ shop. Thường mất 5-15 phút.",
                "Confirmed" => "Shop đã xác nhận đơn hàng và đang chuẩn bị hàng.",
                "Preparing" => "Đơn hàng đang được chuẩn bị và đóng gói cẩn thận.",
                "Shipping" => "Đơn hàng đang trên đường giao đến bạn. Vui lòng chú ý điện thoại!",
                "Delivered" => "Đơn hàng đã được giao thành công. Cảm ơn bạn đã mua hàng! 🎉",
                "Cancelled" => "Đơn hàng đã bị hủy.",
                "Returned" => "Đơn hàng đã được trả lại.",
                _ => "Đang cập nhật thông tin..."
            };
        }

        public async Task<FunctionCallResult> GetPromotionProductsAsync(string userId)
        {
            try
            {
                _logger.LogInformation("[ChatBot] Getting promotion products for user {UserId}", userId);

                var now = DateTime.Now;
                
                // Lấy ProductDiscount đang active
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(pd => pd.IsActive && 
                                 pd.StartDate <= now && 
                                 (pd.EndDate == null || pd.EndDate >= now) &&
                                 pd.ApplyTo == "products" &&
                                 !string.IsNullOrEmpty(pd.ProductIds))
                    .ToListAsync();

                _logger.LogInformation("[ChatBot] Found {Count} active discounts", activeDiscounts.Count);

                if (!activeDiscounts.Any())
                {
                    return new FunctionCallResult
                    {
                        Success = true,
                        Message = "🌸 Hiện tại shop chưa có sản phẩm nào đang khuyến mãi. Anh theo dõi shop để cập nhật chương trình khuyến mãi mới nhất nhé!",
                        Data = new { products = new List<ProductCardDto>() }
                    };
                }

                // Parse ProductIds JSON và lấy danh sách ID
                var productIds = new List<int>();
                foreach (var discount in activeDiscounts)
                {
                    try
                    {
                        _logger.LogInformation("[ChatBot] Parsing ProductIds: {ProductIds}", discount.ProductIds);
                        var ids = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds ?? "[]");
                        if (ids != null)
                        {
                            productIds.AddRange(ids);
                            _logger.LogInformation("[ChatBot] Added {Count} product IDs from discount", ids.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ChatBot] Error parsing ProductIds JSON");
                    }
                }

                productIds = productIds.Distinct().ToList();
                _logger.LogInformation("[ChatBot] Total unique product IDs: {Count}", productIds.Count);

                if (!productIds.Any())
                {
                    return new FunctionCallResult
                    {
                        Success = true,
                        Message = "🌸 Hiện tại shop chưa có sản phẩm nào đang khuyến mãi. Anh theo dõi shop để cập nhật chương trình khuyến mãi mới nhất nhé!",
                        Data = new { products = new List<ProductCardDto>() }
                    };
                }

                // Lấy thông tin sản phẩm
                var products = await _context.Products
                    .Include(p => p.Images)
                    .Where(p => productIds.Contains(p.Id))
                    .Take(6)
                    .ToListAsync();

                _logger.LogInformation("[ChatBot] Found {Count} products from database", products.Count);

                _logger.LogInformation("[ChatBot] Found {Count} products from database", products.Count);

                // Tính giá sau giảm cho từng sản phẩm
                var productList = new List<ProductCardDto>();
                foreach (var product in products)
                {
                    _logger.LogInformation("[ChatBot] Processing product {ProductId}: {ProductName}, Price: {Price}", 
                        product.Id, product.Name, product.Price);

                    // Tìm discount áp dụng cho sản phẩm này
                    var discount = activeDiscounts
                        .FirstOrDefault(d =>
                        {
                            try
                            {
                                var ids = System.Text.Json.JsonSerializer.Deserialize<List<int>>(d.ProductIds ?? "[]");
                                return ids != null && ids.Contains(product.Id);
                            }
                            catch
                            {
                                return false;
                            }
                        });

                    if (discount == null)
                    {
                        _logger.LogWarning("[ChatBot] No discount found for product {ProductId}", product.Id);
                        continue;
                    }

                    _logger.LogInformation("[ChatBot] Applying discount: Type={Type}, Value={Value}", 
                        discount.DiscountType, discount.DiscountValue);

                    decimal finalPrice = product.Price;
                    
                    if (discount.DiscountType.ToLower() == "percentage" || discount.DiscountType.ToLower() == "percent")
                    {
                        var discountAmount = product.Price * (discount.DiscountValue / 100);
                        if (discount.MaxDiscount.HasValue && discountAmount > discount.MaxDiscount.Value)
                        {
                            discountAmount = discount.MaxDiscount.Value;
                        }
                        finalPrice = product.Price - discountAmount;
                    }
                    else if (discount.DiscountType.ToLower() == "fixedamount" || discount.DiscountType.ToLower() == "fixed_amount")
                    {
                        finalPrice = product.Price - discount.DiscountValue;
                    }

                    var imageUrl = !string.IsNullOrEmpty(product.ImageUrl)
                        ? product.ImageUrl  // Ưu tiên ảnh chính
                        : product.Images != null && product.Images.Any() 
                            ? product.Images.First().Url  // Nếu không có ảnh chính thì lấy ảnh phụ
                            : "/images/placeholder.jpg";  // Cuối cùng mới dùng placeholder

                    _logger.LogInformation("[ChatBot] Product card: Id={Id}, Name={Name}, Price={Price}, OriginalPrice={OriginalPrice}, ImageUrl={ImageUrl}", 
                        product.Id, product.Name, finalPrice, product.Price, imageUrl);
                    
                    productList.Add(new ProductCardDto
                    {
                        Id = product.Id,
                        Name = product.Name,
                        Price = finalPrice,
                        OriginalPrice = product.Price,
                        ImageUrl = imageUrl,
                        Url = $"/Product/Details/{product.Id}"
                    });
                }

                _logger.LogInformation("[ChatBot] Created {Count} product cards", productList.Count);

                if (!productList.Any())
                {
                    return new FunctionCallResult
                    {
                        Success = true,
                        Message = "🌸 Hiện tại shop chưa có sản phẩm nào đang khuyến mãi. Anh theo dõi shop để cập nhật chương trình khuyến mãi mới nhất nhé!",
                        Data = new { products = new List<ProductCardDto>() }
                    };
                }

                // Log sản phẩm đầu tiên để debug
                var firstProduct = productList.First();
                _logger.LogInformation("[ChatBot] First product details - Id: {Id}, Name: {Name}, Price: {Price}, OriginalPrice: {OriginalPrice}, ImageUrl: {ImageUrl}, Url: {Url}",
                    firstProduct.Id, firstProduct.Name, firstProduct.Price, firstProduct.OriginalPrice, firstProduct.ImageUrl, firstProduct.Url);

                var result = new FunctionCallResult
                {
                    Success = true,
                    Message = $"Shop có {productList.Count} sản phẩm đang sale bạn nhé:",
                    Data = new { products = productList }
                };

                // Log serialized data để debug
                var jsonData = System.Text.Json.JsonSerializer.Serialize(result.Data);
                _logger.LogInformation("[ChatBot] Serialized data: {JsonData}", jsonData);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBot] Error getting promotion products");
                return new FunctionCallResult
                {
                    Success = false,
                    Message = "❌ Có lỗi xảy ra khi lấy sản phẩm khuyến mãi."
                };
            }
        }
    }
}
