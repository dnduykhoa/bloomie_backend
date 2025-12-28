using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using Bloomie.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Bloomie.Extensions;

namespace Bloomie.Services.Implementations
{
    public class ChatBotService : IChatBotService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGeminiService _geminiService;
        private readonly ILogger<ChatBotService> _logger;
        private readonly IChatBotFunctionService? _functionService;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public ChatBotService(
            ApplicationDbContext context, 
            IGeminiService geminiService, 
            ILogger<ChatBotService> logger,
            IChatBotFunctionService? functionService = null,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _context = context;
            _geminiService = geminiService;
            _logger = logger;
            _functionService = functionService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ChatResponse> ProcessMessageAsync(ChatRequest request)
        {
            // Generate session ID if not provided
            var sessionId = request.SessionId ?? Guid.NewGuid().ToString();

            // Save user message
            var userMessage = new ChatMessage
            {
                SessionId = sessionId,
                Message = request.Message,
                IsBot = false,
                UserId = request.UserId,
                CreatedAt = DateTime.Now
            };
            _context.ChatMessages.Add(userMessage);

            // ===== USE AI-POWERED MODE WITH FUNCTION CALLING =====
            string intent;
            ChatResponse response;
            
            try 
            {
                var result = await ProcessMessageWithAIAndFunctions(request.Message, sessionId, request.UserId);
                (intent, response) = result;
            } 
            catch (Exception ex) 
            {
                _logger.LogWarning($"[ChatBot] AI with functions failed: {ex.Message}, trying fallback");
                try
                {
                    var result = await ProcessMessageWithAI(request.Message, sessionId);
                    (intent, response) = result;
                }
                catch
                {
                    _logger.LogWarning($"[ChatBot] AI failed, using rule-based fallback");
                    var result = await DetectIntentAndRespond(request.Message, sessionId);
                    (intent, response) = result;
                }
            }

            // Save bot response
            var botMessage = new ChatMessage
            {
                SessionId = sessionId,
                Message = response.Message,
                IsBot = true,
                Intent = intent,
                CreatedAt = DateTime.Now,
                Metadata = (response.Products != null && response.Products.Any()) || response.Vouchers != null
                    ? System.Text.Json.JsonSerializer.Serialize(new 
                    { 
                        products = response.Products,
                        vouchers = response.Vouchers
                    }, new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase 
                    }) 
                    : null
            };
            _context.ChatMessages.Add(botMessage);

            await _context.SaveChangesAsync();

            response.SessionId = sessionId;
            response.Intent = intent;

            return response;
        }

        public async Task<string> GetResponseAsync(string userMessage)
        {
            var request = new ChatRequest
            {
                Message = userMessage,
                SessionId = Guid.NewGuid().ToString()
            };
            
            var response = await ProcessMessageAsync(request);
            return response.Message;
        }

        /// <summary>
        /// Process message using AI with Function Calling support
        /// </summary>
        private async Task<(string Intent, ChatResponse Response)> ProcessMessageWithAIAndFunctions(
            string message, 
            string sessionId, 
            string? userId)
        {
            // Get conversation history
            var conversationHistory = await _context.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .ToListAsync();

            // Detect intent
            var intent = await _geminiService.DetectIntentAsync(message);

            // Extract keywords if needed
            var keywords = new List<string>();
            if (intent == "price_inquiry" || intent == "product_search" || intent == "advice")
            {
                keywords = await _geminiService.ExtractProductKeywordsAsync(message);
            }

            // Build database overview
            var databaseOverview = await BuildDatabaseOverviewAsync();
            string productContext = databaseOverview;
            List<ProductSuggestion>? products = null;

            // Query database if keywords found
            if (keywords.Any())
            {
                var productQuery = _context.Products
                    .Where(p => p.IsActive && p.StockQuantity > 0);
                
                var keywordLower = keywords.Select(k => k.ToLower()).ToList();
                productQuery = productQuery.Where(p => 
                    keywordLower.Any(k => 
                        (p.Name != null && p.Name.ToLower().Contains(k)) || 
                        (p.Description != null && p.Description.ToLower().Contains(k))
                    )
                );

                var foundProducts = await productQuery
                    .OrderByDescending(p => p.Id)
                    .Take(10)
                    .ToListAsync();
                
                if (foundProducts.Any())
                {
                    products = await GetProductsWithDiscountAsync(productQuery.Take(10));
                    
                    productContext += $"\n\n=== KẾT QUẢ TÌM KIẾM SẢN PHẨM ===\nTìm thấy {products.Count} sản phẩm:\n\n";
                    foreach (var p in products)
                    {
                        productContext += $"• ID: {p.Id}, Tên: {p.Name}, Giá: {p.Price:#,##0}đ\n";
                    }
                }
            }

            // Generate response with function calling
            var (aiResponse, functionCalls) = await _geminiService.GenerateResponseWithFunctionsAsync(
                message,
                productContext,
                conversationHistory);

            // Execute functions if any
            string? functionResults = null;
            int? cartCount = null;
            List<ProductSuggestion>? functionProducts = null;
            object? functionVouchers = null;
            if (functionCalls != null && functionCalls.Any() && !string.IsNullOrEmpty(userId) && _functionService != null)
            {
                _logger.LogInformation($"[ChatBot] Executing {functionCalls.Count} function(s)");
                (functionResults, cartCount, functionProducts, functionVouchers) = await ExecuteFunctionsAsync(functionCalls, userId);
            }

            // Build final response
            var finalMessage = aiResponse;
            if (!string.IsNullOrEmpty(functionResults))
            {
                // If AI response is just a fallback message and we have function results, use only function results
                if (aiResponse.Contains("Xin lỗi, tôi không hiểu") || aiResponse.Contains("không hiểu"))
                {
                    finalMessage = functionResults;
                }
                else
                {
                    finalMessage = aiResponse + functionResults;
                }
            }

            return (intent, new ChatResponse
            {
                Message = finalMessage,
                Products = functionProducts ?? products,
                QuickReplies = (functionProducts != null || functionVouchers != null) ? null : GenerateQuickReplies(intent), // Không show quick replies nếu đã có product cards hoặc vouchers
                CartCount = cartCount,
                Vouchers = functionVouchers
            });
        }

        /// <summary>
        /// Execute function calls from AI
        /// </summary>
        private async Task<(string Results, int? CartCount, List<ProductSuggestion>? Products, object? Vouchers)> ExecuteFunctionsAsync(List<GeminiFunctionCall> functionCalls, string userId)
        {
            if (_functionService == null)
                return ("", null, null, null);

            var results = new List<string>();
            int? latestCartCount = null;
            List<ProductSuggestion>? functionProducts = null;
            object? functionVouchers = null;

            foreach (var functionCall in functionCalls)
            {
                try
                {
                    _logger.LogInformation($"[ChatBot] Executing function: {functionCall.Name}");

                    FunctionCallResult? result = functionCall.Name switch
                    {
                        "add_to_cart" => await _functionService.AddToCartAsync(userId, new AddToCartParams
                        {
                            ProductName = GetStringArg(functionCall.Args, "productName") ?? "",
                            Quantity = GetIntArg(functionCall.Args, "quantity", 1)
                        }),
                        "get_cart_summary" => await _functionService.GetCartSummaryAsync(userId),
                        "create_order" => await _functionService.CreateOrderAsync(userId, new CreateOrderParams
                        {
                            ShippingAddress = GetStringArg(functionCall.Args, "shippingAddress"),
                            PhoneNumber = GetStringArg(functionCall.Args, "phoneNumber"),
                            PaymentMethod = GetStringArg(functionCall.Args, "paymentMethod", "COD"),
                            Notes = GetStringArg(functionCall.Args, "notes")
                        }),
                        "remove_from_cart" => await _functionService.RemoveFromCartAsync(userId, new RemoveFromCartParams
                        {
                            ProductName = GetStringArg(functionCall.Args, "productName") ?? ""
                        }),
                        "get_user_info" => await _functionService.GetUserInfoAsync(userId),
                        "get_order_status" => await _functionService.GetOrderStatusAsync(userId, new GetOrderStatusParams
                        {
                            OrderId = GetStringArg(functionCall.Args, "orderId") ?? ""
                        }),
                        "get_promotion_products" => await _functionService.GetPromotionProductsAsync(userId),
                        _ => null
                    };

                    if (result != null)
                    {
                        results.Add(result.Message);
                        
                        // Capture latest cart count
                        if (result.CartCount.HasValue)
                        {
                            latestCartCount = result.CartCount.Value;
                        }
                        
                        // Capture products from get_cart_summary or get_promotion_products
                        if (result.Data != null && (functionCall.Name == "get_cart_summary" || functionCall.Name == "get_promotion_products"))
                        {
                            try
                            {
                                var dataJson = System.Text.Json.JsonSerializer.Serialize(result.Data);
                                _logger.LogInformation("[ChatBot] Function data JSON: {Json}", dataJson);
                                
                                var dataDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(dataJson);
                                
                                if (dataDict != null && dataDict.TryGetValue("products", out var productsElement))
                                {
                                    _logger.LogInformation("[ChatBot] Products element: {Element}", productsElement.GetRawText());
                                    
                                    // Deserialize sang ProductCardDto trước
                                    var productCards = System.Text.Json.JsonSerializer.Deserialize<List<ProductCardDto>>(productsElement.GetRawText());
                                    
                                    if (productCards != null && productCards.Any())
                                    {
                                        // Convert sang ProductSuggestion
                                        functionProducts = productCards.Select(p => new ProductSuggestion
                                        {
                                            Id = p.Id,
                                            Name = p.Name ?? "",
                                            Price = p.Price,
                                            OriginalPrice = p.OriginalPrice,
                                            ImageUrl = p.ImageUrl ?? "",
                                            Url = p.Url ?? ""
                                        }).ToList();
                                        
                                        _logger.LogInformation("[ChatBot] Converted {Count} product cards to suggestions", functionProducts.Count);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[ChatBot] Failed to parse products from function result");
                            }
                        }

                        // Capture vouchers from get_user_info
                        if (result.Data != null && functionCall.Name == "get_user_info")
                        {
                            try
                            {
                                var dataJson = System.Text.Json.JsonSerializer.Serialize(result.Data);
                                _logger.LogInformation("[ChatBot] User info data JSON: {Json}", dataJson);
                                
                                var dataDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(dataJson);
                                
                                if (dataDict != null && dataDict.TryGetValue("vouchers", out var vouchersElement))
                                {
                                    _logger.LogInformation("[ChatBot] Vouchers element: {Element}", vouchersElement.GetRawText());
                                    functionVouchers = System.Text.Json.JsonSerializer.Deserialize<object>(vouchersElement.GetRawText());
                                    _logger.LogInformation("[ChatBot] Captured vouchers from get_user_info");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[ChatBot] Failed to parse vouchers from function result");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[ChatBot] Error executing function {functionCall.Name}");
                    results.Add($"❌ Lỗi khi thực hiện: {ex.Message}");
                }
            }

            return (string.Join("\n", results), latestCartCount, functionProducts, functionVouchers);
        }

        private int GetIntArg(Dictionary<string, object> args, string key, int defaultValue = 0)
        {
            if (args.TryGetValue(key, out var value))
            {
                if (value is int intValue)
                    return intValue;
                if (value is long longValue)
                    return (int)longValue;
                if (int.TryParse(value.ToString(), out var parsedValue))
                    return parsedValue;
            }
            return defaultValue;
        }

        private string? GetStringArg(Dictionary<string, object> args, string key, string? defaultValue = null)
        {
            if (args.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }
            return defaultValue;
        }

        /// <summary>
        /// Process message using Gemini AI (NEW AI-POWERED METHOD)
        /// </summary>
        private async Task<(string Intent, ChatResponse Response)> ProcessMessageWithAI(string message, string sessionId)
        {
            // Get conversation history
            var conversationHistory = await _context.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .ToListAsync();

            // Detect intent using Gemini
            var intent = await _geminiService.DetectIntentAsync(message);

            // Extract keywords using Gemini (only if intent needs product search)
            var keywords = new List<string>();
            if (intent == "price_inquiry" || intent == "product_search" || intent == "advice")
            {
                keywords = await _geminiService.ExtractProductKeywordsAsync(message);
            }

            // Build database overview for Gemini (always include this so AI knows about shop)
            var databaseOverview = await BuildDatabaseOverviewAsync();
            
            string productContext = databaseOverview;
            List<ProductSuggestion>? products = null;

            // Query database ONLY if keywords found (meaning user wants to search products)
            if (keywords.Any())
            {
                // Search products by keywords
                var productQuery = _context.Products
                    .Where(p => p.IsActive && p.StockQuantity > 0);
                
                // Build dynamic query with OR logic for better matching
                var keywordLower = keywords.Select(k => k.ToLower()).ToList();
                productQuery = productQuery.Where(p => 
                    keywordLower.Any(k => 
                        (p.Name != null && p.Name.ToLower().Contains(k)) || 
                        (p.Description != null && p.Description.ToLower().Contains(k))
                    )
                );

                var foundProducts = await productQuery
                    .OrderByDescending(p => p.Id)
                    .Take(10)
                    .ToListAsync();
                
                if (foundProducts.Any())
                {
                    products = await GetProductsWithDiscountAsync(productQuery.Take(10));
                    
                    // Append product search results to context
                    productContext += $"\n\n=== KẾT QUẢ TÌM KIẾM SẢN PHẨM ===\nTìm thấy {products.Count} sản phẩm phù hợp với từ khóa: {string.Join(", ", keywords)}\n\n";
                    foreach (var p in products)
                    {
                        if (p.OriginalPrice.HasValue && p.OriginalPrice > p.Price)
                        {
                            var discount = ((p.OriginalPrice.Value - p.Price) / p.OriginalPrice.Value * 100);
                            productContext += $"• {p.Name}: {p.Price:#,##0}đ (Giá gốc: {p.OriginalPrice:#,##0}đ, giảm {discount:0}%)\n";
                        }
                        else
                        {
                            productContext += $"• {p.Name}: {p.Price:#,##0}đ\n";
                        }
                    }
                }
                else
                {
                    productContext += $"\n\n=== KẾT QUẢ TÌM KIẾM ===\nKhông tìm thấy sản phẩm với từ khóa: {string.Join(", ", keywords)}";
                }
            }

            // Query promotions if needed
            if (intent == "promotion_inquiry")
            {
                var now = DateTime.Now;
                var activePromotions = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .ToListAsync();

                if (activePromotions.Any())
                {
                    productContext += $"\n\n=== CHƯƠNG TRÌNH KHUYẾN MÃI ===\nHiện có {activePromotions.Count} chương trình đang áp dụng:\n\n";
                    foreach (var promo in activePromotions)
                    {
                        var discountText = promo.DiscountType == "percent"
                            ? $"{promo.DiscountValue}%"
                            : $"{promo.DiscountValue:#,##0}đ";
                        
                        var endDateText = promo.EndDate.HasValue 
                            ? $" (đến {promo.EndDate.Value:dd/MM/yyyy})" 
                            : "";
                        
                        productContext += $"• {promo.Name}: Giảm {discountText}{endDateText}\n";
                    }
                }
                else
                {
                    productContext += "\n\n=== KHUYẾN MÃI ===\nHiện tại chưa có chương trình khuyến mãi nào đang áp dụng.";
                }
            }
            else if (intent == "shipping" || intent == "advice")
            {
                // Shipping info already in database overview, no need to add
            }
            else if (intent == "greeting")
            {
                // Greeting info already in database overview, no need to add
            }

            // Generate AI response
            var aiResponse = await _geminiService.GenerateResponseAsync(
                message, 
                productContext, 
                conversationHistory);

            return (intent, new ChatResponse
            {
                Message = aiResponse,
                Products = products,
                QuickReplies = GenerateQuickReplies(intent)
            });
        }

        private List<QuickReply>? GenerateQuickReplies(string intent)
        {
            return intent switch
            {
                "greeting" => new List<QuickReply>
                {
                    new QuickReply { Text = "🎂 Hoa sinh nhật", Icon = "🎂" },
                    new QuickReply { Text = "💝 Valentine", Icon = "💝" },
                    new QuickReply { Text = "🎁 Khuyến mãi", Icon = "🎁" }
                },
                "promotion_inquiry" => new List<QuickReply>
                {
                    new QuickReply { Text = "Xem sản phẩm khuyến mãi", Icon = "🛍️" }
                },
                _ => null
            };
        }

        private async Task<(string Intent, ChatResponse Response)> DetectIntentAndRespond(string message, string sessionId)
        {
            // Normalize message - xử lý typo và dấu
            message = NormalizeMessage(message);

            // Get conversation history for context
            var conversationHistory = await _context.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .ToListAsync();

            // PRIORITY-BASED INTENT DETECTION (ưu tiên cao xuống thấp)

            // SPECIAL: Quick reply buttons - xem sản phẩm khuyến mãi
            if (message.ToLower().Contains("xem sản phẩm khuyến mãi") || 
                message.ToLower().Contains("🛍️ xem sản phẩm khuyến mãi"))
            {
                return await ShowPromotionProducts();
            }

            // HIGHEST PRIORITY: Hỏi giá CỤ THỂ (có tên sản phẩm + từ khóa giá)
            // Must check FIRST because "Hoa hồng giá bao nhiêu?" contains question
            if (IsPriceInquiry(message) && HasProductKeywords(message))
            {
                return await HandlePriceInquiry(message);
            }

            // HIGH PRIORITY: Khuyến mãi (câu hỏi trực tiếp về KM)
            if (IsPromotionInquiry(message))
            {
                return await HandlePromotionInquiry();
            }

            // HIGH PRIORITY: Giao hàng (câu hỏi trực tiếp về ship)
            if (IsShippingInquiry(message))
            {
                return ("shipping_inquiry", new ChatResponse
                {
                    Message = "🚚 **Chính sách giao hàng của Bloomie:**\n\n" +
                              "✅ **Miễn phí giao hàng** cho đơn từ 500.000đ trong nội thành\n" +
                              "✅ **Giao hàng nhanh** trong 2-4 giờ khu vực nội thành\n" +
                              "✅ **Cam kết hoa tươi** 100%\n" +
                              "✅ **Giao đúng giờ hẹn**\n\n" +
                              "Bạn muốn đặt hoa giao vào thời gian nào ạ?",
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "Xem sản phẩm", Icon = "🌸" },
                        new QuickReply { Text = "Đặt hàng ngay", Icon = "🛒" }
                    }
                });
            }

            // MEDIUM PRIORITY: Tư vấn (có từ "tư vấn", "gợi ý", "nên chọn", "đẹp nhất")
            if (IsAdviceRequest(message))
            {
                return await HandleAdviceRequest(message);
            }

            // MEDIUM-LOW PRIORITY: Tìm sản phẩm (có từ khóa sản phẩm hoặc occasion)
            if (HasProductKeywords(message) || IsProductSearch(message))
            {
                return await HandleProductSearch(message);
            }

            // LOW PRIORITY: Chào hỏi (chỉ match nếu là chào thuần túy)
            if (IsGreeting(message))
            {
                return ("greeting", new ChatResponse
                {
                    Message = "Xin chào! 👋 Tôi là Bloomie AI - trợ lý ảo của shop hoa Bloomie.\n\nTôi có thể giúp bạn:\n• Tìm kiếm sản phẩm hoa\n• Tư vấn chọn hoa phù hợp\n• Kiểm tra giá và khuyến mãi\n• Hỗ trợ đặt hàng\n\nBạn đang tìm loại hoa nào ạ? 🌸",
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "🎂 Hoa sinh nhật", Icon = "🎂" },
                        new QuickReply { Text = "💝 Valentine", Icon = "💝" },
                        new QuickReply { Text = "🎁 Khuyến mãi", Icon = "🎁" }
                    }
                });
            }

            // LOW PRIORITY: Context-based response (dựa vào lịch sử chat)
            if (conversationHistory.Any())
            {
                var lastBotIntent = conversationHistory
                    .FirstOrDefault(m => m.IsBot && !string.IsNullOrEmpty(m.Intent))?.Intent;

                // Nếu bot vừa hỏi về sản phẩm, và user trả lời ngắn → hiểu là đang tìm sản phẩm
                if (lastBotIntent == "greeting" && message.Split(' ').Length <= 5)
                {
                    return await HandleProductSearch(message);
                }
            }

            // DEFAULT: Không hiểu - gợi ý thông minh hơn
            return ("unknown", new ChatResponse
            {
                Message = "Hmm, tôi chưa hiểu rõ lắm. 🤔 Bạn có thể nói rõ hơn được không?\n\n" +
                          "**Ví dụ:**\n" +
                          "• \"Hoa hồng đỏ giá bao nhiêu?\"\n" +
                          "• \"Tìm hoa sinh nhật cho bạn gái\"\n" +
                          "• \"Có khuyến mãi gì không?\"\n" +
                          "• \"Tư vấn hoa valentine\"\n" +
                          "• \"Giao hàng trong bao lâu?\"",
                QuickReplies = new List<QuickReply>
                {
                    new QuickReply { Text = "Xem sản phẩm hot", Icon = "🔥" },
                    new QuickReply { Text = "Khuyến mãi", Icon = "🎁" },
                    new QuickReply { Text = "Tư vấn", Icon = "💡" }
                }
            });
        }

        // Normalize message - xử lý typo, emoji và Vietnamese text
        private string NormalizeMessage(string message)
        {
            message = message.ToLower().Trim();

            // Remove common emojis and replace with text
            var emojiMap = new Dictionary<string, string>
            {
                { "🎂", " sinh nhật " },
                { "💝", " valentine " },
                { "🎁", " khuyến mãi " },
                { "🌸", " hoa " },
                { "🌹", " hồng " },
                { "🌷", " tulip " },
                { "🌻", " hướng dương " },
                { "💐", " hoa " },
                { "🎊", " khai trương " },
                { "👋", "" },
                { "😊", "" },
                { "❤️", " yêu " },
                { "💕", " yêu " },
                { "🎉", "" },
                { "✨", "" },
                { "🔥", " hot " }
            };

            foreach (var emoji in emojiMap)
            {
                message = message.Replace(emoji.Key, emoji.Value);
            }

            // Common typos
            var typoMap = new Dictionary<string, string>
            {
                { "hogn", "hồng" },
                { "hong", "hồng" },
                { "tuylip", "tulip" },
                { "tylip", "tulip" },
                { "camchuong", "cẩm chướng" },
                { "cam chuong", "cẩm chướng" },
                { "huongduong", "hướng dương" },
                { "huong duong", "hướng dương" },
                { "sinhnhat", "sinh nhật" },
                { "sinh nhat", "sinh nhật" },
                { "khatrong", "khai trương" },
                { "khai trong", "khai trương" },
                { "tanle", "tang lễ" },
                { "tang le", "tang lễ" },
                { "totnghiep", "tốt nghiệp" },
                { "tot nghiep", "tốt nghiệp" }
            };

            foreach (var typo in typoMap)
            {
                message = message.Replace(typo.Key, typo.Value);
            }

            // Clean up extra spaces
            message = System.Text.RegularExpressions.Regex.Replace(message, @"\s+", " ").Trim();

            return message;
        }

        // Check if message has product-related keywords
        private bool HasProductKeywords(string message)
        {
            var productKeywords = ExtractProductKeywords(message);
            return productKeywords.Any();
        }

        // ==================== HELPER METHODS ====================

        private bool IsGreeting(string message)
        {
            // Must be EXACT greeting - not mixed with product questions
            var greetings = new[] { "chào", "hello", "hi", "xin chào", "hey", "chào bạn", "alo", "ê", "hí" };
            
            // Don't match if message contains product/service keywords
            var nonGreetingKeywords = new[] { "hoa", "giá", "bao nhiêu", "mua", "tìm", "khuyến mãi", "giao", "shop" };
            
            return greetings.Any(g => message.Contains(g)) && 
                   !nonGreetingKeywords.Any(k => message.Contains(k));
        }

        private bool IsPriceInquiry(string message)
        {
            // Must have BOTH price keyword AND question structure
            var priceKeywords = new[] { "giá", "bao nhiêu", "giá bao nhiêu", "giá cả", "chi phí", "bao nhiu", "bn" };
            var questionWords = new[] { "?", "bao nhiêu", "giá", "hỏi", "cho biết" };
            
            return priceKeywords.Any(k => message.Contains(k)) && 
                   (questionWords.Any(q => message.Contains(q)) || message.EndsWith("?"));
        }

        private bool IsProductSearch(string message)
        {
            // Broader search - but will be handled AFTER specific intents
            var searchKeywords = new[] { 
                "có", "tìm", "xem", "mua", "cần", "muốn", 
                "show", "search", "tìm kiếm", "tim kiem",
                "cho tôi", "cho toi", "giới thiệu", "gioi thieu"
            };
            return searchKeywords.Any(k => message.Contains(k));
        }

        private bool IsPromotionInquiry(string message)
        {
            var promoKeywords = new[] { 
                "khuyến mãi", "khuyen mai", "km", 
                "giảm giá", "giam gia", 
                "sale", "ưu đãi", "uu dai", 
                "voucher", "mã giảm", "ma giam",
                "discount", "promotion"
            };
            return promoKeywords.Any(k => message.Contains(k));
        }

        private bool IsAdviceRequest(string message)
        {
            var adviceKeywords = new[] { 
                "tư vấn", "tu van", "advice",
                "gợi ý", "goi y", "suggest",
                "nên mua", "nen mua", "nên chọn", "nen chon",
                "phù hợp", "phu hop",
                "tốt nhất", "tot nhat", "đẹp nhất", "dep nhat",
                "giúp tôi", "giup toi", "help me"
            };
            
            // Must have advice keyword OR asking for recommendation
            return adviceKeywords.Any(k => message.Contains(k)) ||
                   (message.Contains("cho") && (message.Contains("người yêu") || message.Contains("bạn gái") || message.Contains("mẹ")));
        }

        private bool IsShippingInquiry(string message)
        {
            var shippingKeywords = new[] { 
                "giao hàng", "giao hang", "ship", 
                "vận chuyển", "van chuyen",
                "giao", "nhận hàng", "nhan hang",
                "delivery", "shipping",
                "bao lâu", "bao lau", "khi nào", "khi nao",
                "mất bao lâu", "mat bao lau"
            };
            
            return shippingKeywords.Any(k => message.Contains(k)) &&
                   !message.Contains("giá"); // Không phải hỏi giá ship
        }

        // ==================== INTENT HANDLERS ====================

        private async Task<(string, ChatResponse)> HandlePriceInquiry(string message)
        {
            // Extract product name from message
            var productKeywords = ExtractProductKeywords(message);

            if (productKeywords.Any())
            {
                // Search products
                var query = _context.Products
                    .Where(p => productKeywords.Any(k => p.Name.ToLower().Contains(k)))
                    .Take(5);
                
                var products = await GetProductsWithDiscountAsync(query);

                if (products.Any())
                {
                    var message_text = $"🌸 Tôi tìm thấy **{products.Count}** sản phẩm phù hợp:\n\n";
                    foreach (var p in products)
                    {
                        message_text += $"• **{p.Name}**: {p.Price:#,##0}đ\n";
                    }
                    message_text += "\nBạn muốn xem chi tiết sản phẩm nào ạ?";

                    return ("price_inquiry", new ChatResponse
                    {
                        Message = message_text,
                        Products = products,
                        QuickReplies = new List<QuickReply>
                        {
                            new QuickReply { Text = "Xem tất cả", Icon = "👀" }
                        }
                    });
                }
            }

            return ("price_inquiry", new ChatResponse
            {
                Message = "Bạn có thể cho tôi biết cụ thể loại hoa nào không ạ?\n\n" +
                          "VD: \"Hoa hồng giá bao nhiêu?\" hoặc \"Giá hoa sinh nhật?\"",
                QuickReplies = new List<QuickReply>
                {
                    new QuickReply { Text = "Hoa hồng", Icon = "🌹" },
                    new QuickReply { Text = "Hoa tulip", Icon = "🌷" },
                    new QuickReply { Text = "Hoa sinh nhật", Icon = "🎂" }
                }
            });
        }

        private async Task<(string, ChatResponse)> HandleProductSearch(string message)
        {
            var productKeywords = ExtractProductKeywords(message);

            var query = _context.Products.AsQueryable();

            // Smart search with multiple strategies
            if (productKeywords.Any())
            {
                // Strategy 1: Exact name match first (highest priority)
                var exactMatchQuery = _context.Products
                    .Where(p => productKeywords.Any(k => p.Name.ToLower() == k))
                    .Take(6);
                
                var exactMatch = await GetProductsWithDiscountAsync(exactMatchQuery);

                if (exactMatch.Any())
                {
                    return ("product_search_exact", new ChatResponse
                    {
                        Message = $"✨ Tìm thấy **{exactMatch.Count}** sản phẩm chính xác:",
                        Products = exactMatch,
                        QuickReplies = new List<QuickReply>
                        {
                            new QuickReply { Text = "Xem thêm", Icon = "👀" },
                            new QuickReply { Text = "Tư vấn thêm", Icon = "💡" }
                        }
                    });
                }

                // Strategy 2: Partial match in name or description
                query = query.Where(p => productKeywords.Any(k => 
                    p.Name.ToLower().Contains(k) || 
                    p.Description.ToLower().Contains(k)));
            }
            else
            {
                // No keywords - show popular/latest products
                query = query.OrderByDescending(p => p.Id);
            }

            var partialMatchQuery = query.Take(6);
            var products = await GetProductsWithDiscountAsync(partialMatchQuery);

            if (products.Any())
            {
                var keywordText = productKeywords.Any() 
                    ? $" liên quan đến **{string.Join(", ", productKeywords.Take(2))}**" 
                    : " phổ biến";

                return ("product_search", new ChatResponse
                {
                    Message = $"🌸 Tìm thấy **{products.Count}** sản phẩm{keywordText}:",
                    Products = products,
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "Xem thêm", Icon = "👀" },
                        new QuickReply { Text = "Tư vấn", Icon = "💡" }
                    }
                });
            }

            // Fallback - suggest categories
            return ("product_search_empty", new ChatResponse
            {
                Message = "Hmm, không tìm thấy sản phẩm phù hợp. �\n\n" +
                          "**Các danh mục phổ biến:**\n" +
                          "🌹 Hoa hồng - Kinh điển, sang trọng\n" +
                          "🌷 Hoa tulip - Thanh lịch, tinh tế\n" +
                          "🌻 Hoa hướng dương - Tươi vui, năng động\n" +
                          "🎂 Hoa sinh nhật - Đa dạng, ý nghĩa\n" +
                          "💝 Hoa tình yêu - Lãng mạn\n" +
                          "🎊 Hoa khai trương - May mắn, thịnh vượng",
                QuickReplies = new List<QuickReply>
                {
                    new QuickReply { Text = "� Hoa hồng", Icon = "�" },
                    new QuickReply { Text = "🎂 Sinh nhật", Icon = "🎂" },
                    new QuickReply { Text = "💝 Valentine", Icon = "💝" }
                }
            });
        }

        private async Task<(string, ChatResponse)> HandlePromotionInquiry()
        {
            var now = DateTime.Now;
            var activePromotions = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .ToListAsync();

            if (activePromotions.Any())
            {
                var message = "🎁 **Khuyến mãi đang diễn ra:**\n\n";
                foreach (var promo in activePromotions)
                {
                    var discountText = promo.DiscountType == "percent"
                        ? $"{promo.DiscountValue}%"
                        : $"{promo.DiscountValue:#,##0}đ";
                    message += $"✨ Giảm {discountText}\n";
                }
                message += "\nÁp dụng cho các sản phẩm đang có trong shop! 🌸";

                return ("promotion_inquiry", new ChatResponse
                {
                    Message = message,
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "Xem sản phẩm khuyến mãi", Icon = "🛍️" }
                    }
                });
            }

            return ("promotion_inquiry", new ChatResponse
            {
                Message = "Hiện tại chưa có chương trình khuyến mãi nào. 😊\n\n" +
                          "Bạn có thể theo dõi fanpage hoặc đăng ký nhận thông báo để cập nhật khuyến mãi sớm nhất!",
                QuickReplies = new List<QuickReply>
                {
                    new QuickReply { Text = "Xem sản phẩm", Icon = "🌸" }
                }
            });
        }

        private async Task<(string, ChatResponse)> ShowPromotionProducts()
        {
            var now = DateTime.Now;
            
            // Lấy tất cả sản phẩm còn hàng
            var allProducts = await _context.Products
                .Where(p => p.IsActive && p.StockQuantity > 0)
                .Take(50) // Lấy nhiều hơn để filter
                .ToListAsync();

            // Lấy tất cả discount đang active
            var activeDiscounts = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .ToListAsync();

            // Filter sản phẩm có discount
            var productIdsWithDiscount = new List<int>();
            foreach (var discount in activeDiscounts)
            {
                if (discount.ApplyTo == "all")
                {
                    productIdsWithDiscount.AddRange(allProducts.Select(p => p.Id));
                }
                else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                {
                    try
                    {
                        var ids = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                        if (ids != null)
                            productIdsWithDiscount.AddRange(ids);
                    }
                    catch { }
                }
            }

            productIdsWithDiscount = productIdsWithDiscount.Distinct().Take(10).ToList();

            if (productIdsWithDiscount.Any())
            {
                // Tạo query cho các sản phẩm có discount
                var productsQuery = _context.Products.Where(p => productIdsWithDiscount.Contains(p.Id));
                
                // Tính giá sau khi giảm cho từng sản phẩm
                var productSuggestions = await GetProductsWithDiscountAsync(productsQuery);

                return ("promotion_products", new ChatResponse
                {
                    Message = $"🛍️ **Sản phẩm đang khuyến mãi:**\n\nHiện có {productSuggestions.Count()} sản phẩm đang giảm giá. Bạn hãy xem và chọn sản phẩm yêu thích nhé! 💝",
                    Products = productSuggestions
                });
            }

            return ("promotion_products", new ChatResponse
            {
                Message = "Hiện tại không có sản phẩm nào đang khuyến mãi. 😊\n\n" +
                          "Bạn có thể xem các sản phẩm khác hoặc quay lại sau nhé!",
                QuickReplies = new List<QuickReply>
                {
                    new QuickReply { Text = "Xem tất cả sản phẩm", Icon = "🌸" }
                }
            });
        }

        private async Task<(string, ChatResponse)> HandleAdviceRequest(string message)
        {
            var keywords = ExtractProductKeywords(message);

            // Detect occasion and recipient for better advice
            if (keywords.Contains("sinh nhật") || keywords.Contains("birthday"))
            {
                var birthdayQuery = _context.Products
                    .Where(p => p.IsActive && (
                               p.Name.ToLower().Contains("sinh nhật") || 
                               p.Name.ToLower().Contains("happy birthday") ||
                               p.Name.ToLower().Contains("birthday") ||
                               (p.Description != null && p.Description.ToLower().Contains("sinh nhật")) ||
                               (p.ProductCategories != null && p.ProductCategories.Any(pc => pc.Category!.Name.ToLower().Contains("sinh nhật")))))
                    .OrderByDescending(p => p.Id)
                    .Take(6);
                
                var products = await GetProductsWithDiscountAsync(birthdayQuery);
                
                // Nếu không tìm thấy sản phẩm sinh nhật, lấy sản phẩm phổ biến
                if (products.Count == 0)
                {
                    var fallbackQuery = _context.Products
                        .Where(p => p.IsActive)
                        .OrderByDescending(p => p.Id)
                        .Take(6);
                    products = await GetProductsWithDiscountAsync(fallbackQuery);
                }

                return ("advice_birthday", new ChatResponse
                {
                    Message = "🎂 **Tư vấn hoa sinh nhật:**\n\n" +
                              "✨ **Dành cho nữ:**\n" +
                              "🌹 Hoa hồng phấn - Ngọt ngào, nữ tính\n" +
                              "🌷 Hoa tulip - Thanh lịch, tinh tế\n" +
                              "💐 Mix pastel - Dịu dàng, đáng yêu\n\n" +
                              "✨ **Dành cho nam:**\n" +
                              "🌻 Hoa hướng dương - Nam tính, khỏe khoắn\n" +
                              "🎋 Hoa lan - Sang trọng, lịch lãm\n" +
                              "💛 Màu vàng/cam - Mạnh mẽ, tươi sáng\n\n" +
                              "✨ **Dành cho trẻ em:**\n" +
                              "� Mix nhiều màu sắc rực rỡ\n" +
                              "🧸 Kèm gấu bông hoặc bóng bay\n\n" +
                              "Dưới đây là một số gợi ý phù hợp:",
                    Products = products,
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "Xem tất cả hoa sinh nhật", Icon = "🎂" }
                    }
                });
            }

            if (keywords.Contains("valentine") || keywords.Contains("tình yêu"))
            {
                var valentineQuery = _context.Products
                    .Where(p => p.IsActive && (
                               p.Name.ToLower().Contains("hồng") || 
                               p.Name.ToLower().Contains("valentine") || 
                               p.Name.ToLower().Contains("tình yêu") ||
                               p.Name.ToLower().Contains("rose") ||
                               (p.Description != null && p.Description.ToLower().Contains("tình yêu")) ||
                               (p.Description != null && p.Description.ToLower().Contains("valentine"))))
                    .OrderByDescending(p => p.Id)
                    .Take(6);
                
                var products = await GetProductsWithDiscountAsync(valentineQuery);
                
                // Nếu không tìm thấy sản phẩm valentine, lấy sản phẩm hoa hồng hoặc phổ biến
                if (products.Count == 0)
                {
                    var fallbackQuery = _context.Products
                        .Where(p => p.IsActive)
                        .OrderByDescending(p => p.Id)
                        .Take(6);
                    products = await GetProductsWithDiscountAsync(fallbackQuery);
                }

                return ("advice_valentine", new ChatResponse
                {
                    Message = "💝 **Tư vấn hoa Valentine/Tình yêu:**\n\n" +
                              "🌹 **Hoa hồng đỏ (12-99-108 bông):**\n" +
                              "• 12 bông: Tình yêu trọn vẹn 12 tháng\n" +
                              "• 99 bông: Yêu mãi mãi, vĩnh cửu\n" +
                              "• 108 bông: Cầu hôn, kết hôn\n\n" +
                              "🤍 **Hoa hồng trắng:**\n" +
                              "• Tình yêu thuần khiết, chân thành\n" +
                              "• Phù hợp tỏ tình lần đầu\n\n" +
                              "💖 **Hoa tulip:**\n" +
                              "• Tình yêu hoàn hảo\n" +
                              "• Sang trọng, tinh tế\n\n" +
                              "🎀 **Mix hoa hồng nhiều màu:**\n" +
                              "• Đa dạng cảm xúc\n" +
                              "• Độc đáo, ấn tượng\n\n" +
                              "💡 **Lưu ý:** Nên đặt trước 1-2 ngày để đảm bảo hoa tươi nhất!",
                    Products = products,
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "Xem hoa hồng đỏ", Icon = "🌹" },
                        new QuickReply { Text = "Xem hoa tulip", Icon = "🌷" }
                    }
                });
            }

            if (keywords.Contains("khai trương"))
            {
                return ("advice_opening", new ChatResponse
                {
                    Message = "🎊 **Tư vấn hoa khai trương:**\n\n" +
                              "🌻 **Hoa hướng dương:**\n" +
                              "• Tượng trưng thịnh vượng, phát đạt\n" +
                              "• Màu vàng rực rỡ, may mắn\n\n" +
                              "🌸 **Lan hồ điệp:**\n" +
                              "• Sang trọng, đẳng cấp\n" +
                              "• Giữ được lâu (1-2 tuần)\n\n" +
                              "💐 **Kệ hoa lớn:**\n" +
                              "• Nổi bật, thu hút\n" +
                              "• Nhiều màu sắc rực rỡ\n\n" +
                              "📦 **Giao hàng:**\n" +
                              "• Miễn phí trong nội thành\n" +
                              "• Có thể giao sớm sáng để kịp lễ\n" +
                              "• Kèm thiệp chúc mừng theo yêu cầu",
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "Xem kệ hoa", Icon = "🎊" },
                        new QuickReply { Text = "Xem lan hồ điệp", Icon = "🌸" }
                    }
                });
            }

            if (keywords.Contains("mẹ") || keywords.Contains("8/3") || keywords.Contains("20/10"))
            {
                return ("advice_mother", new ChatResponse
                {
                    Message = "💐 **Tư vấn hoa tặng mẹ/phụ nữ:**\n\n" +
                              "🌹 **Hoa hồng phấn:**\n" +
                              "• Biểu tượng sự dịu dàng\n" +
                              "• Thể hiện tình cảm gia đình\n\n" +
                              "🌷 **Hoa tulip:**\n" +
                              "• Thanh lịch, nhẹ nhàng\n" +
                              "• Màu pastel dịu mắt\n\n" +
                              "🌸 **Hoa cẩm chướng:**\n" +
                              "• Tượng trưng tình mẫu tử\n" +
                              "• Giá cả phải chăng, giữ lâu\n\n" +
                              "💝 **Mix hoa pastel:**\n" +
                              "• Phối nhiều loại hoa đẹp\n" +
                              "• Nữ tính, tinh tế",
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "Xem hoa cẩm chướng", Icon = "�" },
                        new QuickReply { Text = "Xem hoa tulip", Icon = "🌷" }
                    }
                });
            }

            if (keywords.Contains("tang lễ") || keywords.Contains("chia buồn"))
            {
                return ("advice_funeral", new ChatResponse
                {
                    Message = "🕊️ **Tư vấn hoa tang lễ/chia buồn:**\n\n" +
                              "🤍 **Hoa cúc trắng:**\n" +
                              "• Truyền thống Á Đông\n" +
                              "• Tôn kính, tiễn đưa\n\n" +
                              "🌼 **Hoa lily trắng:**\n" +
                              "• Thuần khiết, thánh thiện\n" +
                              "• Phổ biến ở tang lễ Công giáo\n\n" +
                              "💐 **Vòng hoa/Kệ hoa:**\n" +
                              "• Màu trắng, vàng nhạt\n" +
                              "• Kèm băng rôn chia buồn\n\n" +
                              "📌 **Lưu ý:**\n" +
                              "• Tránh màu sắc rực rỡ\n" +
                              "• Giao hàng đúng giờ\n" +
                              "• Có thiệp chia buồn trang trọng",
                    QuickReplies = new List<QuickReply>
                    {
                        new QuickReply { Text = "Liên hệ tư vấn", Icon = "📞" }
                    }
                });
            }

            // General advice with more details
            return ("advice_general", new ChatResponse
            {
                Message = "💐 **Tư vấn chọn hoa chi tiết:**\n\n" +
                          "Để tư vấn chính xác nhất, bạn vui lòng cho tôi biết:\n\n" +
                          "1️⃣ **Dịp gì?**\n" +
                          "   • Sinh nhật, Valentine, 8/3, 20/10\n" +
                          "   • Khai trương, tốt nghiệp\n" +
                          "   • Cưới, tang lễ, thăm bệnh\n\n" +
                          "2️⃣ **Tặng cho ai?**\n" +
                          "   • Người yêu (nam/nữ)\n" +
                          "   • Mẹ, bạn bè, đồng nghiệp\n" +
                          "   • Sếp, khách hàng\n\n" +
                          "3️⃣ **Ngân sách?**\n" +
                          "   • Dưới 300k: Bó nhỏ xinh\n" +
                          "   • 300k-500k: Bó/giỏ trung\n" +
                          "   • 500k-1 triệu: Bó/giỏ lớn\n" +
                          "   • Trên 1 triệu: Kệ hoa, hộp hoa sang trọng\n\n" +
                          "4️⃣ **Màu sắc yêu thích?**\n" +
                          "   • Đỏ: Mạnh mẽ, tình yêu\n" +
                          "   • Hồng: Dịu dàng, nữ tính\n" +
                          "   • Trắng: Tinh khôi, thanh lịch\n" +
                          "   • Vàng/cam: Tươi vui, năng động\n" +
                          "   • Tím: Bí ẩn, sang trọng",
                QuickReplies = new List<QuickReply>
                {
                    new QuickReply { Text = "🎂 Sinh nhật", Icon = "🎂" },
                    new QuickReply { Text = "💝 Valentine", Icon = "💝" },
                    new QuickReply { Text = "🎊 Khai trương", Icon = "🎊" },
                    new QuickReply { Text = "🌸 Ngày 8/3", Icon = "🌸" }
                }
            });
        }

        private List<string> ExtractProductKeywords(string message)
        {
            var keywords = new List<string>();
            
            // Mở rộng danh sách loại hoa với nhiều biến thể và typo phổ biến
            var flowerTypes = new Dictionary<string, string[]>
            {
                { "hồng", new[] { "hồng", "hong", "rose", "hoa hồng", "hoa hong" } },
                { "tulip", new[] { "tulip", "tulíp", "tu lip", "hoa tulip" } },
                { "cẩm chướng", new[] { "cẩm chướng", "cam chuong", "carnation", "hoa cẩm chướng" } },
                { "ly", new[] { "ly", "loa kèn", "lily", "hoa ly", "hoa loa kèn" } },
                { "hướng dương", new[] { "hướng dương", "huong duong", "sunflower", "hoa hướng dương" } },
                { "lan", new[] { "lan", "orchid", "hoa lan", "phong lan" } },
                { "cúc", new[] { "cúc", "cuc", "chrysanthemum", "hoa cúc", "đồng tiền", "dong tien" } },
                { "baby", new[] { "baby", "baby breath", "hơi thở em bé", "hoi tho em be" } },
                { "sen", new[] { "sen", "lotus", "hoa sen" } },
                { "đào", new[] { "đào", "dao", "hoa đào", "mai đào" } },
                { "mai", new[] { "mai", "hoa mai", "mai vàng" } },
                { "violet", new[] { "violet", "tím", "hoa tím" } },
                { "lavender", new[] { "lavender", "hoa oải hương", "oải hương" } },
                { "thược dược", new[] { "thược dược", "thuoc duoc", "peony" } }
            };

            // Mở rộng dịp với nhiều biến thể
            var occasions = new Dictionary<string, string[]>
            {
                { "sinh nhật", new[] { "sinh nhật", "sinh nhat", "birthday", "happy birthday", "chúc mừng sinh nhật" } },
                { "valentine", new[] { "valentine", "14/2", "lễ tình nhân", "le tinh nhan", "ngày valentine" } },
                { "khai trương", new[] { "khai trương", "khai truong", "opening", "mở cửa hàng", "khai trương" } },
                { "tốt nghiệp", new[] { "tốt nghiệp", "tot nghiep", "graduation", "lễ tốt nghiệp" } },
                { "cưới", new[] { "cưới", "cuoi", "wedding", "đám cưới", "dam cuoi", "lễ cưới" } },
                { "tang lễ", new[] { "tang lễ", "tang le", "funeral", "đám tang", "dam tang", "chia buồn", "chia buon" } },
                { "tình yêu", new[] { "tình yêu", "tinh yeu", "người yêu", "nguoi yeu", "yêu", "crush", "bạn gái", "ban gai", "bạn trai" } },
                { "mẹ", new[] { "mẹ", "me", "mom", "mother", "má", "mẹ yêu", "ngày của mẹ" } },
                { "8/3", new[] { "8/3", "8 tháng 3", "quốc tế phụ nữ", "quoc te phu nu", "ngày phụ nữ" } },
                { "20/10", new[] { "20/10", "20 tháng 10", "phụ nữ việt nam" } },
                { "giáng sinh", new[] { "giáng sinh", "giang sinh", "christmas", "noel", "xmas" } },
                { "tết", new[] { "tết", "tet", "tết nguyên đán", "tet nguyen dan", "xuân", "xuan", "năm mới" } }
            };

            // Thêm từ khóa về màu sắc
            var colors = new Dictionary<string, string[]>
            {
                { "đỏ", new[] { "đỏ", "do", "red", "màu đỏ" } },
                { "trắng", new[] { "trắng", "trang", "white", "màu trắng" } },
                { "hồng", new[] { "hồng", "hong", "pink", "màu hồng" } },
                { "vàng", new[] { "vàng", "vang", "yellow", "màu vàng" } },
                { "tím", new[] { "tím", "tim", "purple", "màu tím" } },
                { "cam", new[] { "cam", "orange", "màu cam" } }
            };

            // Extract flower types
            foreach (var flower in flowerTypes)
            {
                if (flower.Value.Any(variant => message.Contains(variant)))
                {
                    keywords.Add(flower.Key);
                }
            }

            // Extract occasions
            foreach (var occasion in occasions)
            {
                if (occasion.Value.Any(variant => message.Contains(variant)))
                {
                    keywords.Add(occasion.Key);
                }
            }

            // Extract colors
            foreach (var color in colors)
            {
                if (color.Value.Any(variant => message.Contains(variant)))
                {
                    keywords.Add(color.Key);
                }
            }

            return keywords;
        }

        // Build comprehensive database overview for AI context
        private async Task<string> BuildDatabaseOverviewAsync()
        {
            var totalProducts = await _context.Products.CountAsync(p => p.IsActive);
            var totalCategories = await _context.Categories.CountAsync();
            var categories = await _context.Categories
                .GroupBy(c => c.Type)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToListAsync();
            
            var now = DateTime.Now;
            var activePromotions = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .CountAsync();

            // Get top flower types
            var topFlowers = await _context.Products
                .Where(p => p.IsActive && p.StockQuantity > 0)
                .Select(p => p.Name)
                .Distinct()
                .Take(10)
                .ToListAsync();

            var overview = $@"=== THÔNG TIN SHOP BLOOMIE (DATABASE) ===

📊 THỐNG KÊ CƠ SỞ DỮ LIỆU:
- Tổng số sản phẩm: {totalProducts} sản phẩm đang kinh doanh
- Danh mục: {totalCategories} danh mục (Chủ đề, Đối tượng, Hình dáng)
- Khuyến mãi: {activePromotions} chương trình đang hoạt động

🌸 LOẠI HOA PHỔ BIẾN TRONG DATABASE:
{string.Join(", ", topFlowers)}

🚚 CHÍNH SÁCH GIAO HÀNG:
- Phí ship: 30,000đ (MIỄN PHÍ với đơn từ 500,000đ)
- Thời gian: 2-4 giờ nội thành, 1-2 ngày ngoại thành
- Phạm vi: Giao hàng toàn quốc
- Cam kết: Hoa tươi 100%, giao đúng giờ, đổi trả nếu không hài lòng

💝 CHÍNH SÁCH BÁN HÀNG:
- Thanh toán: COD, chuyển khoản, VNPAY
- Bảo hành: Đổi trả trong 24h nếu hoa không tươi
- Hỗ trợ: Tư vấn miễn phí 24/7";

            return overview;
        }

        // Helper method to get products with discount info
        private async Task<List<ProductSuggestion>> GetProductsWithDiscountAsync(IQueryable<Product> query)
        {
            var now = DateTime.Now;
            var products = await query.ToListAsync();
            
            var productSuggestions = new List<ProductSuggestion>();
            
            foreach (var p in products)
            {
                // Get active discount for this product
                var discount = await _context.ProductDiscounts
                    .Where(d => d.IsActive && 
                                d.StartDate <= now && 
                                (d.EndDate == null || d.EndDate >= now) &&
                                (d.ApplyTo == "all" || 
                                 (d.ProductIds != null && d.ProductIds.Contains(p.Id.ToString()))))
                    .OrderByDescending(d => d.Priority)
                    .FirstOrDefaultAsync();

                decimal finalPrice = p.Price;
                decimal? originalPrice = null;

                if (discount != null)
                {
                    originalPrice = p.Price;
                    
                    if (discount.DiscountType == "percent")
                    {
                        finalPrice = p.Price * (1 - discount.DiscountValue / 100);
                    }
                    else // fixed amount
                    {
                        finalPrice = p.Price - discount.DiscountValue;
                    }
                    
                    finalPrice = Math.Max(0, finalPrice); // Ensure non-negative
                }

                productSuggestions.Add(new ProductSuggestion
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = finalPrice,
                    OriginalPrice = originalPrice,
                    ImageUrl = p.ImageUrl ?? p.Images?.FirstOrDefault()?.Url,
                    Url = $"/Product/Details/{p.Id}"
                });
            }

            return productSuggestions;
        }
    }
}
