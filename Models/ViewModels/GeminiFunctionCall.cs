using System.Text.Json.Serialization;

namespace Bloomie.Models.ViewModels
{
    /// <summary>
    /// Represents a function call from Gemini AI
    /// </summary>
    public class GeminiFunctionCall
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("args")]
        public Dictionary<string, object> Args { get; set; } = new();
    }

    /// <summary>
    /// Function declarations for Gemini AI
    /// </summary>
    public static class GeminiFunctionDeclarations
    {
        public static object GetFunctionDeclarations()
        {
            var functions = new List<object>
            {
                new
                {
                    name = "add_to_cart",
                    description = "Thêm sản phẩm vào giỏ hàng. Sử dụng khi user muốn mua hoặc thêm sản phẩm vào giỏ.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["productName"] = new
                            {
                                type = "string",
                                description = "Tên sản phẩm muốn mua (lấy từ danh sách sản phẩm đã tìm được hoặc từ câu hỏi của user)"
                            },
                            ["quantity"] = new
                            {
                                type = "integer",
                                description = "Số lượng sản phẩm muốn mua (mặc định: 1)"
                            }
                        },
                        required = new[] { "productName", "quantity" }
                    }
                },
                new
                {
                    name = "get_cart_summary",
                    description = "Xem giỏ hàng hiện tại. Sử dụng khi user hỏi 'giỏ hàng có gì', 'xem giỏ hàng', 'tổng tiền bao nhiêu'.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>()
                    }
                },
                new
                {
                    name = "create_order",
                    description = "Đặt hàng từ giỏ hàng hiện tại. Sử dụng khi user nói 'đặt hàng', 'mua luôn', 'thanh toán'. CẦN phải có địa chỉ giao hàng.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["shippingAddress"] = new
                            {
                                type = "string",
                                description = "Địa chỉ giao hàng (bắt buộc)"
                            },
                            ["phoneNumber"] = new
                            {
                                type = "string",
                                description = "Số điện thoại liên hệ (optional, sẽ dùng số của user nếu không cung cấp)"
                            },
                            ["paymentMethod"] = new
                            {
                                type = "string",
                                @enum = new[] { "COD", "VNPAY" },
                                description = "Phương thức thanh toán: COD (tiền mặt khi nhận hàng) hoặc VNPAY (chuyển khoản online)"
                            },
                            ["notes"] = new
                            {
                                type = "string",
                                description = "Ghi chú đơn hàng (optional)"
                            }
                        },
                        required = new[] { "shippingAddress" }
                    }
                },
                new
                {
                    name = "remove_from_cart",
                    description = "Xóa sản phẩm khỏi giỏ hàng. Sử dụng khi user nói 'xóa', 'bỏ sản phẩm ra'.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["productName"] = new
                            {
                                type = "string",
                                description = "Tên sản phẩm cần xóa"
                            }
                        },
                        required = new[] { "productName" }
                    }
                },
                new
                {
                    name = "get_user_info",
                    description = "Lấy thông tin user hiện tại (tên, email, số điện thoại, địa chỉ). Sử dụng khi cần thông tin để đặt hàng.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>()
                    }
                },
                new
                {
                    name = "get_order_status",
                    description = "Kiểm tra trạng thái đơn hàng. Sử dụng khi user hỏi về đơn hàng, trạng thái giao hàng, hoặc gửi thông tin đơn hàng.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["orderId"] = new
                            {
                                type = "string",
                                description = "Mã đơn hàng (OrderId) cần kiểm tra. Ví dụ: ORD-20250101-001"
                            }
                        },
                        required = new[] { "orderId" }
                    }
                },
                new
                {
                    name = "get_promotion_products",
                    description = "Lấy danh sách sản phẩm đang khuyến mãi/giảm giá. Sử dụng khi user hỏi 'sản phẩm nào đang sale', 'hoa nào giảm giá', 'có khuyến mãi gì'.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>()
                    }
                }
            };

            return new
            {
                function_declarations = functions
            };
        }
    }
}
