using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class ShipperDashboardViewModel
    {
        // Thống kê đơn hàng hôm nay
        public int TotalOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int InProgressOrders { get; set; }
        public int FailedOrders { get; set; }
        
        // Thống kê tuần này
        public int WeekTotalOrders { get; set; }
        public int WeekCompletedOrders { get; set; }
        
        // Thống kê tháng này
        public int MonthTotalOrders { get; set; }
        public int MonthCompletedOrders { get; set; }
        
        // Tiền COD
        public decimal CodCollectedToday { get; set; }
        public decimal CodCollectedWeek { get; set; }
        public decimal CodCollectedMonth { get; set; }
        
        // Danh sách đơn COD hôm nay
        public List<Order> CodOrders { get; set; } = new List<Order>();
        
        // Đánh giá (nếu có)
        public double AverageRating { get; set; }
        public int TotalReviews { get; set; }
    }
}
