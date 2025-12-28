using Bloomie.Models.Entities;

namespace Bloomie.Services.Interfaces
{
    public interface IShipperAssignmentService
    {
        /// <summary>
        /// Tự động phân công đơn hàng cho shipper theo thuật toán Round Robin
        /// </summary>
        Task<bool> AssignOrderToShipperAsync(int orderId);
        
        /// <summary>
        /// Lấy danh sách shipper có thể nhận đơn (đang làm việc và chưa quá tải)
        /// </summary>
        Task<List<ShipperProfile>> GetAvailableShippersAsync();
        
        /// <summary>
        /// Cập nhật số đơn hiện tại của shipper
        /// </summary>
        Task UpdateShipperStatsAsync(string userId);
        
        /// <summary>
        /// Hủy phân công và gán lại cho shipper khác (khi timeout)
        /// </summary>
        Task<bool> ReassignOrderAsync(int orderId);
        
        /// <summary>
        /// Xác nhận shipper đã nhận hoa (hủy Hangfire job)
        /// </summary>
        Task<bool> ConfirmPickupAsync(int orderId, string shipperId);
        
        /// <summary>
        /// Tự động phân công shipper cho đơn đặt trước có ngày giao = hôm nay
        /// </summary>
        Task AutoAssignPreOrdersForToday();
        
        /// <summary>
        /// Kiểm tra đơn hàng URGENT (còn < 1 giờ đến giờ giao mà chưa có shipper confirm)
        /// </summary>
        Task CheckUrgentOrders();
    }
}
