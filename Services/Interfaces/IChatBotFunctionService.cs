using Bloomie.Models.ViewModels;

namespace Bloomie.Services.Interfaces
{
    /// <summary>
    /// Service for executing functions called by AI chatbot
    /// </summary>
    public interface IChatBotFunctionService
    {
        /// <summary>
        /// Add product to shopping cart
        /// </summary>
        Task<FunctionCallResult> AddToCartAsync(string userId, AddToCartParams parameters);

        /// <summary>
        /// Remove product from shopping cart
        /// </summary>
        Task<FunctionCallResult> RemoveFromCartAsync(string userId, RemoveFromCartParams parameters);

        /// <summary>
        /// Get current cart summary
        /// </summary>
        Task<FunctionCallResult> GetCartSummaryAsync(string userId);

        /// <summary>
        /// Create order from current cart
        /// </summary>
        Task<FunctionCallResult> CreateOrderAsync(string userId, CreateOrderParams parameters);

        /// <summary>
        /// Apply voucher to cart/order
        /// </summary>
        Task<FunctionCallResult> ApplyVoucherAsync(string userId, ApplyVoucherParams parameters);

        /// <summary>
        /// Get user information (name, address, phone)
        /// </summary>
        Task<FunctionCallResult> GetUserInfoAsync(string userId);

        /// <summary>
        /// Get order status by order ID
        /// </summary>
        Task<FunctionCallResult> GetOrderStatusAsync(string userId, GetOrderStatusParams parameters);

        /// <summary>
        /// Get products on promotion/discount
        /// </summary>
        Task<FunctionCallResult> GetPromotionProductsAsync(string userId);
    }
}
