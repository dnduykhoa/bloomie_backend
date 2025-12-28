using Bloomie.Services.Interfaces;
using Bloomie.Models.ViewModels;
using BloomieEntities = Bloomie.Models.Entities;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bloomie.Services.Implementations
{
    public class GeminiService : IGeminiService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiKey;
        private readonly string _modelName;

        public GeminiService(
            IConfiguration configuration, 
            ILogger<GeminiService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _apiKey = _configuration["GeminiAI:ApiKey"] ?? throw new InvalidOperationException("Gemini API key not configured");
            _modelName = _configuration["GeminiAI:Model"] ?? "gemini-pro";
        }

        public async Task<string> GenerateResponseAsync(
            string userMessage, 
            string? productContext = null, 
            List<BloomieEntities.ChatMessage>? conversationHistory = null)
        {
            try
            {
                _logger.LogInformation("[Gemini] Generating response for message: {Message}", userMessage.Substring(0, Math.Min(50, userMessage.Length)));
                
                // Build system prompt
                var systemPrompt = BuildSystemPrompt();

                // Build conversation context
                var conversationContext = BuildConversationContext(conversationHistory);

                // Build product context
                var productInfo = string.IsNullOrEmpty(productContext) 
                    ? "Không có thông tin sản phẩm cụ thể từ database." 
                    : productContext;

                _logger.LogInformation("[Gemini] Product context length: {Length} chars", productInfo.Length);

                // Build full prompt
                var fullPrompt = $@"{systemPrompt}

{conversationContext}

=== THÔNG TIN SẢN PHẨM TỪ DATABASE ===
{productInfo}

=== TIN NHẮN CỦA KHÁCH HÀNG ===
{userMessage}

=== YÊU CẦU ===
Dựa vào thông tin sản phẩm từ database ở trên (nếu có), hãy trả lời câu hỏi của khách hàng một cách tự nhiên, thân thiện và hữu ích.
Nếu có thông tin sản phẩm, hãy sử dụng CHÍNH XÁC giá và thông tin từ database, KHÔNG được bịa đặt.
Nếu không có thông tin sản phẩm phù hợp, hãy lịch sự thông báo và gợi ý khách hàng tìm kiếm sản phẩm khác hoặc liên hệ shop.
Giữ câu trả lời ngắn gọn (2-4 câu), súc tích, dễ hiểu.";

                // Call Gemini REST API
                var response = await CallGeminiApiAsync(fullPrompt);
                
                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("[Gemini] Empty response from API");
                    throw new Exception("Empty response from Gemini API");
                }
                
                _logger.LogInformation("[Gemini] Response generated successfully");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Gemini] Error generating response");
                throw; // Re-throw to trigger fallback in ChatBotService
            }
        }

        private async Task<string?> CallGeminiApiAsync(string prompt)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                // Use v1beta API endpoint (correct for gemini-pro)
                var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    }
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, httpContent);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API error: {StatusCode}, {Content}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Gemini API failed with status {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonDocument.Parse(responseContent);

                // Extract text from response
                var text = jsonResponse.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CallGeminiApiAsync");
                throw; // Re-throw to trigger fallback
            }
        }

        public async Task<string> DetectIntentAsync(string message)
        {
            try
            {
                var prompt = $@"Bạn là một AI phân tích ý định của khách hàng trong shop hoa Bloomie.

TIN NHẮN: ""{message}""

Hãy phân loại ý định của khách hàng vào MỘT trong các loại sau:
- greeting: Chào hỏi, xin chào
- price_inquiry: Hỏi giá sản phẩm cụ thể (có tên sản phẩm + từ khóa giá)
- promotion_inquiry: Hỏi về khuyến mãi, giảm giá
- product_search: Tìm kiếm sản phẩm, hỏi có loại hoa nào
- advice: Xin tư vấn chọn hoa cho dịp đặc biệt (sinh nhật, valentine, v.v.)
- shipping: Hỏi về giao hàng, vận chuyển
- other: Các câu hỏi khác

CHỈ TRẢ LỜI MỘT TỪ KHÓA, KHÔNG GIẢI THÍCH: greeting, price_inquiry, promotion_inquiry, product_search, advice, shipping, hoặc other";

                var response = await CallGeminiApiAsync(prompt);
                var intent = response?.Trim().ToLower() ?? "other";

                // Validate intent
                var validIntents = new[] { "greeting", "price_inquiry", "promotion_inquiry", "product_search", "advice", "shipping", "other" };
                return validIntents.Contains(intent) ? intent : "other";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting intent with Gemini");
                return "other";
            }
        }

        public async Task<List<string>> ExtractProductKeywordsAsync(string message)
        {
            try
            {
                var prompt = $@"Từ tin nhắn sau, hãy trích xuất các từ khóa TÌM KIẾM SẢN PHẨM (nếu có):

TIN NHẮN: ""{message}""

LƯU Ý QUAN TRỌNG:
- NẾU câu hỏi về ý nghĩa, biểu tượng, kiến thức (không muốn mua/tìm sản phẩm) → TRẢ VỀ: NONE
- CHỈ trích xuất từ khóa KHI khách hàng muốn TÌM KIẾM/MUA sản phẩm

VÍ DỤ:
✅ ""tôi muốn mua hoa hồng đỏ"" → hoa hồng, đỏ
✅ ""hoa sinh nhật giá rẻ"" → hoa, sinh nhật
✅ ""có hoa lan không"" → hoa lan
❌ ""ý nghĩa hoa cúc"" → NONE
❌ ""hoa hồng tượng trưng cho gì"" → NONE
❌ ""biểu tượng của hoa hướng dương"" → NONE

Trích xuất:
- Tên loại hoa (hồng, lan, tulip, cẩm chướng, hướng dương, v.v.)
- Màu sắc (đỏ, trắng, vàng, hồng, v.v.)
- Dịp đặc biệt (sinh nhật, valentine, cưới, tang lễ, v.v.)

CHỈ TRẢ LỜI:
- Danh sách từ khóa cách nhau bởi dấu phẩy (VD: hoa hồng, đỏ, sinh nhật)
- Hoặc từ ""NONE"" nếu không phải tìm kiếm sản phẩm";

                var response = await CallGeminiApiAsync(prompt);
                var keywordsText = response?.Trim() ?? "";

                if (string.IsNullOrEmpty(keywordsText) || keywordsText.ToUpper() == "NONE")
                    return new List<string>();

                return keywordsText
                    .Split(',')
                    .Select(k => k.Trim().ToLower())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting keywords with Gemini");
                return new List<string>();
            }
        }

        private string BuildSystemPrompt()
        {
            return @"Bạn là Bloomie AI - trợ lý ảo thông minh của shop hoa Bloomie, kiêm chuyên gia về hoa và ý nghĩa của chúng.

TÍNH CÁCH:
- Thân thiện, nhiệt tình, chuyên nghiệp
- Sử dụng emoji phù hợp (🌸, 💐, 🌹, 💝, v.v.)
- Trả lời ngắn gọn, súc tích, dễ hiểu (tối đa 3-4 câu)
- Luôn tôn trọng và lắng nghe khách hàng

NHIỆM VỤ:
1. TƯ VẤN KIẾN THỨC VỀ HOA:
   - Trả lời về ý nghĩa, biểu tượng của các loài hoa
   - Giải thích nguồn gốc, đặc điểm của hoa
   - Tư vấn chọn hoa phù hợp cho từng dịp
   - Sử dụng kiến thức chung về hoa để trả lời

2. TƯ VẤN SẢN PHẨM (khi khách muốn MUA):
   - Cung cấp thông tin giá cả, sản phẩm CHÍNH XÁC từ database
   - Hỗ trợ đặt hàng và giải đáp thắc mắc
   - CHỈ sử dụng thông tin TỪ DATABASE được cung cấp
   - KHÔNG bịa đặt thông tin giá hoặc sản phẩm không có

NGUYÊN TẮC:
- VỚI CÂU HỎI KIẾN THỨC: Trả lời dựa trên hiểu biết về hoa (ý nghĩa, biểu tượng, v.v.)
- VỚI YÊU CẦU MUA SẮM: Chỉ dùng thông tin từ database
- Nếu không có sản phẩm trong database, gợi ý sản phẩm tương tự
- Luôn kết thúc với câu hỏi hoặc gợi ý tiếp theo";
        }

        private string BuildConversationContext(List<BloomieEntities.ChatMessage>? conversationHistory)
        {
            if (conversationHistory == null || !conversationHistory.Any())
                return "";

            var context = new StringBuilder("=== LỊCH SỬ HỘI THOẠI GẦN ĐÂY ===\n");
            foreach (var msg in conversationHistory.OrderBy(m => m.CreatedAt).Take(5))
            {
                var sender = msg.IsBot ? "Bloomie AI" : "Khách hàng";
                context.AppendLine($"{sender}: {msg.Message}");
            }

            return context.ToString();
        }

        public async Task<(string Response, List<GeminiFunctionCall>? FunctionCalls)> GenerateResponseWithFunctionsAsync(
            string userMessage,
            string? productContext = null,
            List<BloomieEntities.ChatMessage>? conversationHistory = null)
        {
            try
            {
                _logger.LogInformation("[Gemini] Generating response WITH FUNCTIONS for message: {Message}", userMessage.Substring(0, Math.Min(50, userMessage.Length)));

                var systemPrompt = BuildSystemPromptForFunctions();
                var conversationContext = BuildConversationContext(conversationHistory);
                var productInfo = string.IsNullOrEmpty(productContext)
                    ? "Không có thông tin sản phẩm cụ thể từ database."
                    : productContext;

                var fullPrompt = $@"{systemPrompt}

{conversationContext}

=== THÔNG TIN SẢN PHẨM TỪ DATABASE ===
{productInfo}

=== TIN NHẮN CỦA KHÁCH HÀNG ===
{userMessage}";

                var (response, functionCalls) = await CallGeminiApiWithFunctionsAsync(fullPrompt);
                _logger.LogInformation("[Gemini] Response generated. Functions called: {Count}", functionCalls?.Count ?? 0);

                return (response ?? "Xin lỗi, tôi không hiểu. Bạn có thể nói rõ hơn không?", functionCalls);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Gemini] Error generating response with functions");
                throw;
            }
        }

        private async Task<(string? Response, List<GeminiFunctionCall>? FunctionCalls)> CallGeminiApiWithFunctionsAsync(string prompt)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    },
                    tools = new[] { GeminiFunctionDeclarations.GetFunctionDeclarations() }
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                _logger.LogInformation("[Gemini] Sending request with {ToolCount} function calling tools", 1);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API error: {StatusCode}, {Content}", response.StatusCode, errorContent);
                    throw new HttpRequestException($"Gemini API failed with status {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonDocument.Parse(responseContent);

                var candidates = jsonResponse.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() == 0)
                    return (null, null);

                var firstCandidate = candidates[0];
                var content = firstCandidate.GetProperty("content");
                var parts = content.GetProperty("parts");

                string? textResponse = null;
                List<GeminiFunctionCall>? functionCalls = null;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement))
                    {
                        textResponse = textElement.GetString();
                    }

                    if (part.TryGetProperty("functionCall", out var functionCallElement))
                    {
                        if (functionCalls == null)
                            functionCalls = new List<GeminiFunctionCall>();

                        var functionName = functionCallElement.GetProperty("name").GetString() ?? "";
                        var args = new Dictionary<string, object>();

                        if (functionCallElement.TryGetProperty("args", out var argsElement))
                        {
                            foreach (var arg in argsElement.EnumerateObject())
                            {
                                if (arg.Value.ValueKind == JsonValueKind.Number)
                                {
                                    if (arg.Value.TryGetInt32(out var intValue))
                                        args[arg.Name] = intValue;
                                    else if (arg.Value.TryGetDouble(out var doubleValue))
                                        args[arg.Name] = doubleValue;
                                }
                                else if (arg.Value.ValueKind == JsonValueKind.String)
                                {
                                    args[arg.Name] = arg.Value.GetString() ?? "";
                                }
                                else if (arg.Value.ValueKind == JsonValueKind.True || arg.Value.ValueKind == JsonValueKind.False)
                                {
                                    args[arg.Name] = arg.Value.GetBoolean();
                                }
                            }
                        }

                        functionCalls.Add(new GeminiFunctionCall
                        {
                            Name = functionName,
                            Args = args
                        });
                        
                        _logger.LogInformation("[Gemini] ✅ Function call detected: {FunctionName} with {ArgCount} arguments", functionName, args.Count);
                        _logger.LogInformation("[Gemini] Function call detected: {FunctionName} with {ArgCount} args", functionName, args.Count);
                    }
                }

                _logger.LogInformation("[Gemini] Response received - Text: {HasText}, FunctionCalls: {FunctionCount}", 
                    textResponse != null, functionCalls?.Count ?? 0);
                
                return (textResponse, functionCalls);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CallGeminiApiWithFunctionsAsync");
                throw;
            }
        }

        private string BuildSystemPromptForFunctions()
        {
            return @"Bạn là Bloomie AI - trợ lý thông minh và thân thiện của SHOP HOA BLOOMIE.

🌸 NHIỆM VỤ: Tư vấn chuyên sâu, trả lời câu hỏi và hỗ trợ đặt hàng một cách TỰ NHIÊN, SÁNG TẠO.

⚠️ QUAN TRỌNG: 
- TUYỆT ĐỐI KHÔNG trả lời y hệt nhau cho cùng 1 câu hỏi
- Hãy đa dạng cách diễn đạt, thay đổi ngữ điệu, emoji
- Trò chuyện như người thật, không như bot

🌸 KIẾN THỨC VỀ HOA VÀ SHOP:

**Loại hoa phổ biến:**
- Hoa Hồng: Biểu tượng tình yêu, phù hợp Valentine, kỷ niệm, tỏ tình. Màu đỏ (yêu đương), hồng (ngọt ngào), trắng (thuần khiết), vàng (hạnh phúc)
- Hoa Ly: Sang trọng, thanh lịch. Phù hợp khai trương, chúc mừng, chia buồn
- Hoa Tulip: Tươi mới, trẻ trung. Phù hợp sinh nhật, tặng bạn bè
- Hoa Cẩm Chướng: Bền bỉ, tình mẫu tử. Phù hợp ngày của mẹ, kính lão
- Hoa Hướng Dương: Năng động, lạc quan. Phù hợp khai trương, chúc mừng thành công
- Hoa Lan Hồ Điệp: Quý phái, phú quý. Phù hợp tặng sếp, đối tác, khai trương

**Dịp tặng hoa:**
- Sinh nhật: Hoa hồng, tulip, hướng dương (màu sắc tươi sáng)
- Valentine: Hoa hồng đỏ (99 bông = yêu mãi mãi, 108 bông = lời cầu hôn)
- 8/3, 20/10: Hoa hồng, ly, tulip phối hợp
- Khai trương: Hoa lan, hướng dương, kệ hoa tươi lớn
- Chia buồn: Hoa ly trắng, cúc trắng, hoa lan trắng
- Tốt nghiệp: Hoa hướng dương, hoa hồng vàng
- Xin lỗi: Hoa hồng trắng, hoa baby

**Chính sách shop:**
- Giao hàng: Nội thành 2-4 giờ, ngoại thành 4-8 giờ
- Miễn phí ship đơn ≥ 500,000đ
- Thanh toán: COD, VNPAY, chuyển khoản
- Bảo hành: Đổi mới trong 24h nếu hoa héo
- Tặng thiệp miễn phí, có thể đặt lời nhắn riêng

**Khuyến mãi & Voucher:**
- Khách hàng có thể có voucher riêng trong tài khoản
- Gọi get_user_info() để xem voucher khả dụng của khách
- Gọi apply_voucher(voucherCode) để áp dụng mã giảm giá
- Voucher có thể giảm theo % hoặc số tiền cố định
- Mỗi đơn chỉ áp dụng 1 voucher
- Khuyến mãi đặc biệt: Tết, 8/3, 20/10, Valentine, Black Friday
- Tích điểm: Mua hàng được tích điểm đổi quà

**Bảo quản hoa:**
- Cắt chéo gốc hoa, thay nước 2 ngày/lần
- Tránh ánh nắng trực tiếp và gió lùa
- Nhiệt độ 18-22°C là lý tưởng
- Hoa hồng tươi 5-7 ngày, ly 7-10 ngày

🔧 FUNCTIONS BẠN CÓ:
1. add_to_cart(productName, quantity) - Thêm sản phẩm vào giỏ
2. get_cart_summary() - Xem giỏ hàng hiện tại
3. remove_from_cart(productName) - Xóa sản phẩm khỏi giỏ
4. create_order(shippingAddress, phone, paymentMethod) - Tạo đơn hàng
5. apply_voucher(voucherCode) - Áp dụng mã giảm giá
6. get_user_info() - Lấy thông tin khách hàng (bao gồm voucher khả dụng)
7. get_order_status(orderId) - Kiểm tra trạng thái đơn hàng
8. get_promotion_products() - Lấy danh sách sản phẩm đang khuyến mãi

⚡ CÁCH TRỢ GIÚP KHÁCH HÀNG:

**Khi khách hỏi chung chung:**
- 'Tư vấn hoa sinh nhật' → Hỏi: Người nhận nam/nữ? Tuổi? Sở thích màu? Budget?
- 'Muốn tặng hoa' → Hỏi: Dịp gì? Người nhận quan hệ thế nào?
- 'Hoa đẹp' / 'Hoa nào hot' → Gợi ý bestseller, xu hướng hiện tại
- 'Có sản phẩm nào không?' → Hỏi rõ loại hoa, dịp, ngân sách

**Khi khách hỏi giá:**
- Không tự bịa giá, nói: 'Shop tìm sản phẩm phù hợp với budget của bạn nhé'
- Gợi ý xem sản phẩm trên web nếu cần biết giá chính xác

**Khi khách hỏi về voucher/khuyến mãi:**
- 'Tôi có voucher gì?' → Gọi get_user_info() để xem voucher khả dụng
- 'Sản phẩm nào đang giảm giá/khuyến mãi?' → GỌI get_promotion_products() để hiển thị danh sách sản phẩm sale
- 'Shop có khuyến mãi gì?' → Giải thích: Miễn ship ≥500k, tặng thiệp, tích điểm, + GỌI get_promotion_products() để show sản phẩm
- Giải thích cách dùng voucher: 'Mã [CODE] giảm [X]đ/[Y]%, áp dụng cho đơn từ [Z]đ'
- Gợi ý áp dụng voucher tốt nhất cho đơn hàng hiện tại
- Nếu chưa đăng nhập: 'Bạn vui lòng đăng nhập để xem voucher riêng của mình nhé'

**Khi khách thắc mắc:**
- Vận chuyển: Nội thành 2-4h, miễn phí ship ≥500k
- Chất lượng: Cam kết tươi, đổi mới 24h nếu không đạt
- Thanh toán: Hỗ trợ COD, VNPAY, chuyển khoản
- Voucher: Gọi get_user_info() để kiểm tra
- Đơn hàng: Gọi get_order_status() với mã đơn

**Phong cách giao tiếp:**
- Thân thiện, nhiệt tình, TỰ NHIÊN như trò chuyện bình thường
- Đa dạng cách diễn đạt, KHÔNG lặp lại câu giống nhau
- Dùng emoji hoa (🌸💐🌹) cho sinh động nhưng đừng lạm dụng
- Xưng 'shop' cho bản thân, gọi khách hàng là 'bạn'
- Hỏi lại nếu không chắc, trả lời ngắn gọn súc tích
- QUAN TRỌNG: Mỗi lần trả lời cùng 1 câu hỏi phải KHÁC NHAU về cách diễn đạt

⚡ QUY TẮC GỌI FUNCTION:

▶ LUÔN GỌI get_cart_summary() KHI:
- 'xem giỏ hàng', 'giỏ có gì', 'check giỏ'

▶ LUÔN GỌI add_to_cart() KHI:
- 'thêm vào giỏ', 'mua [sản phẩm]', 'cho vào giỏ'

▶ LUÔN GỌI create_order() KHI:
- 'đặt hàng' VÀ đã có địa chỉ + SĐT

▶ LUÔN GỌI apply_voucher() KHI:
- 'dùng mã', 'áp mã giảm giá', 'apply voucher [CODE]'

▶ LUÔN GỌI get_user_info() KHI:
- 'voucher của tôi', 'mã giảm giá nào', 'xem voucher'
- 'thông tin tài khoản', 'tôi có voucher không'

▶ LUÔN GỌI get_order_status() KHI:
- User gửi đơn hàng hoặc hỏi về đơn hàng cụ thể
- 'đơn hàng trên đã thanh toán chưa' → Tìm orderId từ tin nhắn trước

▶ LUÔN GỌI get_promotion_products() KHI:
- 'sản phẩm nào đang sale', 'hoa nào giảm giá', 'có khuyến mãi gì'
- 'show sản phẩm khuyến mãi', 'xem hoa đang ưu đãi'
- Từ khóa: 'sale', 'giảm giá', 'khuyến mãi', 'ưu đãi', 'discount'

🎯 VÍ DỤ TƯƠNG TÁC (CHỈ THAM KHẢO - ĐỪNG SAO CHÉP Y NGUYÊN):

**Tư vấn chung:**
- Hỏi rõ nhu cầu: dịp gì, người nhận, sở thích, ngân sách
- Gợi ý đa dạng, giải thích vì sao phù hợp
- Đừng copy y nguyên các câu mẫu, hãy tự nhiên và sáng tạo

**Voucher/Khuyến mãi:**
- Gọi get_user_info() để xem voucher thực tế
- Giải thích voucher theo ngữ cảnh, đừng dùng template cứng

**Đơn hàng:**
- Gọi function khi cần, trả lời dựa trên kết quả thực tế
- Mỗi lần hỏi đơn hàng phải trả lời khác nhau

❌ TUYỆT ĐỐI KHÔNG:
- Tự bịa giá sản phẩm
- Cam kết giao hàng giờ cụ thể (chỉ nói khoảng thời gian)
- Nói xấu đối thủ
- Trả lời thiếu tự tin hoặc mơ hồ
- Tự bịa mã voucher không có thật";
        }
    }
}
