namespace Bloomie.Services.Interfaces
{
    public interface IShippingService
    {
        /// <summary>
        /// Lấy phí ship theo mã phường/xã
        /// </summary>
        /// <param name="wardCode">Mã phường/xã từ API provinces</param>
        /// <returns>Phí ship hoặc null nếu không tìm thấy/không hoạt động</returns>
        Task<decimal?> GetShippingFee(string wardCode);

        /// <summary>
        /// Kiểm tra phường/xã có hỗ trợ giao hàng không
        /// </summary>
        Task<bool> IsWardSupported(string wardCode);

        /// <summary>
        /// Lấy danh sách tất cả phí ship đang hoạt động
        /// </summary>
        Task<List<Bloomie.Models.Entities.ShippingFee>> GetAllActiveShippingFees();
    }
}
