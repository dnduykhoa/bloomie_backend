namespace Bloomie.Models.ViewModels
{
    public class DashboardViewModel
    {
        // Users
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int LockedUsers { get; set; }
        public int DeletedUsers { get; set; }
        public int NewUsersToday { get; set; }
        public int NewUsersThisWeek { get; set; }
        public int NewUsersThisMonth { get; set; }
        
        // Thống kê đăng nhập
        public int LoginsTodayCount { get; set; }
        public int LoginsThisWeekCount { get; set; }
        
        // Orders
        public int TotalOrders { get; set; }
        public int PendingOrders { get; set; }
        public int ShippingOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int OrdersToday { get; set; }
        public int OrdersThisWeek { get; set; }
        public int OrdersThisMonth { get; set; }
        
        // Revenue
        public decimal TotalRevenue { get; set; }
        public decimal RevenueToday { get; set; }
        public decimal RevenueThisWeek { get; set; }
        public decimal RevenueThisMonth { get; set; }
        
        // Products
        public int TotalProducts { get; set; }
        public int LowStockProducts { get; set; }
        public int OutOfStockProducts { get; set; }
        
        // Promotions
        public int TotalPromotions { get; set; }
        public int ActivePromotions { get; set; }
        public int ExpiringSoonPromotions { get; set; }
        
        // Categories
        public int TotalCategories { get; set; }
        
        // Purchase Orders
        public decimal TotalPurchaseCost { get; set; }
        public decimal PurchaseCostThisMonth { get; set; }
        
        // Reviews
        public int TotalReviews { get; set; }
        public double AverageRating { get; set; }
        public int PendingReviews { get; set; }
        
        // Thống kê theo role
        public Dictionary<string, int> UsersByRole { get; set; } = new();
        
        // Top thiết bị đăng nhập
        public List<DeviceStatistic> TopDevices { get; set; } = new();
        
        // Hoạt động gần đây
        public List<RecentActivity> RecentActivities { get; set; } = new();
        
        // Top products
        public List<TopProductItem> TopSellingProducts { get; set; } = new();
        
        // Recent orders
        public List<RecentOrderItem> RecentOrders { get; set; } = new();
        
        // Chart data
        public List<ChartDataPoint> RevenueChartData { get; set; } = new();
        public List<ChartDataPoint> OrderStatusChartData { get; set; } = new();
        public List<ProfitChartDataPoint> ProfitChartData { get; set; } = new();
        public List<ChartDataPoint> PaymentMethodChartData { get; set; } = new();
        public List<ChartDataPoint> CategoryRevenueChartData { get; set; } = new();
        public List<ChartDataPoint> PeakHoursChartData { get; set; } = new();
        public List<ChartDataPoint> OrderCompletionRateChartData { get; set; } = new();
        public List<ChartDataPoint> CustomerGrowthChartData { get; set; } = new();
    }

    public class UserStatisticsViewModel
    {
        public int TotalRegistrations { get; set; }
        public int EmailConfirmedUsers { get; set; }
        public int TwoFactorEnabledUsers { get; set; }
        public List<UserGrowthData> UserGrowthChart { get; set; } = new();
        public List<RoleDistribution> RoleDistribution { get; set; } = new();
        public List<UserActivitySummary> TopActiveUsers { get; set; } = new();
    }

    public class SystemHealthViewModel
    {
        public string DatabaseStatus { get; set; } = "Unknown";
        public string EmailServiceStatus { get; set; } = "Unknown";
        public long DatabaseSize { get; set; }
        public int ActiveSessions { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public List<SystemAlert> SystemAlerts { get; set; } = new();
        public DateTime LastBackup { get; set; }
    }

    public class ReportsViewModel
    {
        public List<MonthlyReport> MonthlyUserReports { get; set; } = new();
        public List<SecurityReport> SecurityReports { get; set; } = new();
        public List<ActivityReport> ActivityReports { get; set; } = new();
        public DateTime ReportGeneratedAt { get; set; } = DateTime.UtcNow;
    }

    // Supporting classes
    public class DeviceStatistic
    {
        public string DeviceType { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class RecentActivity
    {
        public string UserName { get; set; } = "";
        public string Action { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string IpAddress { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class UserGrowthData
    {
        public string Month { get; set; } = "";
        public int NewUsers { get; set; }
        public int TotalUsers { get; set; }
    }

    public class RoleDistribution
    {
        public string RoleName { get; set; } = "";
        public int UserCount { get; set; }
        public double Percentage { get; set; }
    }

    public class UserActivitySummary
    {
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
        public int LoginCount { get; set; }
        public DateTime LastLogin { get; set; }
        public int PageViews { get; set; }
    }

    public class SystemAlert
    {
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class MonthlyReport
    {
        public string Month { get; set; } = "";
        public int NewUsers { get; set; }
        public int DeletedUsers { get; set; }
        public int TotalLogins { get; set; }
        public int SecurityIncidents { get; set; }
    }

    public class SecurityReport
    {
        public string EventType { get; set; } = "";
        public int Count { get; set; }
        public DateTime Date { get; set; }
        public string Severity { get; set; } = "";
    }

    public class ActivityReport
    {
        public string ActivityType { get; set; } = "";
        public int Count { get; set; }
        public DateTime Date { get; set; }
    }

    public class TopProductItem
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string? ImageUrl { get; set; }
        public int TotalSold { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class RecentOrderItem
    {
        public string OrderId { get; set; } = "";
        public string UserId { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "";
        public DateTime OrderDate { get; set; }
    }

    public class ChartDataPoint
    {
        public string Label { get; set; } = "";
        public double Value { get; set; }
    }

    public class ProfitChartDataPoint
    {
        public string Label { get; set; } = "";
        public double Revenue { get; set; }
        public double Cost { get; set; }
        public double Profit { get; set; }
    }
}
