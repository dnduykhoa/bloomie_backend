using System.Text.Json.Serialization;

namespace Bloomie.Models.ViewModels
{
    /// <summary>
    /// DTO for product information in chat responses
    /// </summary>
    public class ProductCardDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("price")]
        public decimal Price { get; set; }
        
        [JsonPropertyName("originalPrice")]
        public decimal? OriginalPrice { get; set; }
        
        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Base class for all function call results
    /// </summary>
    public class FunctionCallResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        
        [JsonPropertyName("data")]
        public object? Data { get; set; }
        
        [JsonPropertyName("cartCount")]
        public int? CartCount { get; set; }
    }

    /// <summary>
    /// Parameters for AddToCart function
    /// </summary>
    public class AddToCartParams
    {
        [JsonPropertyName("productName")]
        public string ProductName { get; set; } = string.Empty;
        
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;
    }

    /// <summary>
    /// Parameters for CreateOrder function
    /// </summary>
    public class CreateOrderParams
    {
        [JsonPropertyName("shippingAddress")]
        public string? ShippingAddress { get; set; }
        
        [JsonPropertyName("phoneNumber")]
        public string? PhoneNumber { get; set; }
        
        [JsonPropertyName("paymentMethod")]
        public string PaymentMethod { get; set; } = "COD"; // COD, VNPAY
        
        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Parameters for ApplyVoucher function
    /// </summary>
    public class ApplyVoucherParams
    {
        [JsonPropertyName("voucherCode")]
        public string VoucherCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parameters for RemoveFromCart function
    /// </summary>
    public class RemoveFromCartParams
    {
        [JsonPropertyName("productName")]
        public string ProductName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parameters for GetOrderStatus function
    /// </summary>
    public class GetOrderStatusParams
    {
        [JsonPropertyName("orderId")]
        public string OrderId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Order status data
    /// </summary>
    public class OrderStatusData
    {
        [JsonPropertyName("orderId")]
        public string OrderId { get; set; } = string.Empty;
        
        [JsonPropertyName("orderDate")]
        public DateTime OrderDate { get; set; }
        
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        
        [JsonPropertyName("statusText")]
        public string StatusText { get; set; } = string.Empty;
        
        [JsonPropertyName("totalAmount")]
        public decimal TotalAmount { get; set; }
        
        [JsonPropertyName("shippingAddress")]
        public string? ShippingAddress { get; set; }
        
        [JsonPropertyName("phoneNumber")]
        public string? PhoneNumber { get; set; }
        
        [JsonPropertyName("paymentMethod")]
        public string? PaymentMethod { get; set; }
        
        [JsonPropertyName("items")]
        public List<OrderItemSummary> Items { get; set; } = new();
        
        [JsonPropertyName("trackingInfo")]
        public string? TrackingInfo { get; set; }
    }

    public class OrderItemSummary
    {
        [JsonPropertyName("productName")]
        public string ProductName { get; set; } = string.Empty;
        
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }
        
        [JsonPropertyName("price")]
        public decimal Price { get; set; }
        
        [JsonPropertyName("subtotal")]
        public decimal Subtotal { get; set; }
    }

    /// <summary>
    /// Cart summary data
    /// </summary>
    public class CartSummaryData
    {
        [JsonPropertyName("totalItems")]
        public int TotalItems { get; set; }
        
        [JsonPropertyName("subtotal")]
        public decimal Subtotal { get; set; }
        
        [JsonPropertyName("discount")]
        public decimal Discount { get; set; }
        
        [JsonPropertyName("shippingFee")]
        public decimal ShippingFee { get; set; }
        
        [JsonPropertyName("total")]
        public decimal Total { get; set; }
        
        [JsonPropertyName("items")]
        public List<CartItemSummary> Items { get; set; } = new();
    }

    public class CartItemSummary
    {
        [JsonPropertyName("productId")]
        public int ProductId { get; set; }
        
        [JsonPropertyName("productName")]
        public string ProductName { get; set; } = string.Empty;
        
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }
        
        [JsonPropertyName("price")]
        public decimal Price { get; set; }
        
        [JsonPropertyName("subtotal")]
        public decimal Subtotal { get; set; }
    }

    /// <summary>
    /// Available functions for AI to call
    /// </summary>
    public static class AvailableFunctions
    {
        public const string AddToCart = "add_to_cart";
        public const string RemoveFromCart = "remove_from_cart";
        public const string GetCartSummary = "get_cart_summary";
        public const string CreateOrder = "create_order";
        public const string ApplyVoucher = "apply_voucher";
        public const string GetUserInfo = "get_user_info";
        public const string GetOrderStatus = "get_order_status";
        public const string GetPromotionProducts = "get_promotion_products";
    }
}
