using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminDashboardController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;

        public AdminDashboardController(
            UserManager<ApplicationUser> userManager, 
            RoleManager<IdentityRole> roleManager, 
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
        }

        // Dashboard chính
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            // Mặc định: 7 ngày gần đây
            if (!startDate.HasValue || !endDate.HasValue)
            {
                endDate = DateTime.Now.Date;
                startDate = endDate.Value.AddDays(-6); // 7 ngày (bao gồm hôm nay)
            }
            
            ViewBag.StartDate = startDate.Value.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate.Value.ToString("yyyy-MM-dd");
            
            var viewModel = await GetDashboardData(startDate.Value, endDate.Value);
            return View(viewModel);
        }

        // Thống kê user chi tiết
        public async Task<IActionResult> UserStatistics()
        {
            var viewModel = await GetUserStatisticsData();
            return View(viewModel);
        }

        // Tình trạng hệ thống
        public async Task<IActionResult> SystemHealth()
        {
            var viewModel = await GetSystemHealthData();
            return View(viewModel);
        }

        // Báo cáo tổng hợp
        public async Task<IActionResult> Reports()
        {
            var viewModel = await GetReportsData();
            return View(viewModel);
        }

        // Helper methods
        private async Task<DashboardViewModel> GetDashboardData(DateTime startDate, DateTime endDate)
        {
            var today = DateTime.UtcNow.Date;
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            var monthStart = new DateTime(today.Year, today.Month, 1);
            
            // Đảm bảo endDate bao gồm cả ngày cuối
            var endDateTime = endDate.AddDays(1).AddSeconds(-1);

            // Orders statistics - Lọc theo khoảng thời gian
            var allOrders = await _context.Orders
                .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDateTime)
                .ToListAsync();
            var completedOrders = allOrders.Where(o => o.Status == "Hoàn thành" || o.Status == "Đã giao").ToList();
            
            // Tổng số đơn (không lọc) cho các stat cards
            var totalAllOrders = await _context.Orders.ToListAsync();
            
            return new DashboardViewModel
            {
                // Users (không lọc theo thời gian - hiển thị tổng)

                TotalUsers = await _userManager.Users.CountAsync(u => !u.IsDeleted),
                ActiveUsers = await _userManager.Users.CountAsync(u => !u.IsDeleted && (u.LockoutEnd == null || u.LockoutEnd < DateTime.UtcNow)),
                LockedUsers = await _userManager.Users.CountAsync(u => !u.IsDeleted && u.LockoutEnd != null && u.LockoutEnd > DateTime.UtcNow),
                DeletedUsers = await _userManager.Users.CountAsync(u => u.IsDeleted),
                
                NewUsersToday = await _userManager.Users.CountAsync(u => u.CreatedAt.Date == today),
                NewUsersThisWeek = await _userManager.Users.CountAsync(u => u.CreatedAt.Date >= weekStart),
                NewUsersThisMonth = await _userManager.Users.CountAsync(u => u.CreatedAt.Date >= monthStart),
                
                LoginsTodayCount = await _context.LoginHistories.CountAsync(l => l.LoginTime.Date == today),
                LoginsThisWeekCount = await _context.LoginHistories.CountAsync(l => l.LoginTime.Date >= weekStart),
                
                // Orders - Theo khoảng thời gian được chọn
                TotalOrders = allOrders.Count,
                PendingOrders = allOrders.Count(o => o.Status == "Chờ xác nhận"),
                ShippingOrders = allOrders.Count(o => o.Status == "Đang giao"),
                CompletedOrders = completedOrders.Count,
                
                OrdersToday = allOrders.Count(o => o.OrderDate.Date == today),
                OrdersThisWeek = allOrders.Count(o => o.OrderDate.Date >= weekStart),
                OrdersThisMonth = allOrders.Count(o => o.OrderDate.Date >= monthStart),
                
                // Revenue - Theo khoảng thời gian được chọn
                TotalRevenue = completedOrders.Sum(o => o.TotalAmount),
                RevenueToday = completedOrders.Where(o => o.OrderDate.Date == today).Sum(o => o.TotalAmount),
                RevenueThisWeek = completedOrders.Where(o => o.OrderDate.Date >= weekStart).Sum(o => o.TotalAmount),
                RevenueThisMonth = completedOrders.Where(o => o.OrderDate.Date >= monthStart).Sum(o => o.TotalAmount),
                
                // Products
                TotalProducts = await _context.Products.CountAsync(p => p.IsActive),
                LowStockProducts = await _context.Products.CountAsync(p => p.IsActive && p.StockQuantity < 10),
                OutOfStockProducts = await _context.Products.CountAsync(p => p.IsActive && p.StockQuantity == 0),
                
                // Promotions
                TotalPromotions = await _context.Promotions.CountAsync(),
                ActivePromotions = await _context.Promotions.CountAsync(p => p.IsActive && (p.EndDate == null || p.EndDate >= DateTime.Now)),
                ExpiringSoonPromotions = await _context.Promotions.CountAsync(p => p.IsActive && p.EndDate != null && p.EndDate >= DateTime.Now && p.EndDate <= DateTime.Now.AddDays(7)),
                
                // Categories
                TotalCategories = await _context.Categories.CountAsync(),
                
                // Purchase Orders (Chi phí nhập hàng)
                TotalPurchaseCost = await _context.PurchaseOrderDetails.SumAsync(pod => pod.Quantity * pod.UnitPrice),
                PurchaseCostThisMonth = await _context.PurchaseOrderDetails
                    .Where(pod => pod.PurchaseOrder.OrderDate.Date >= monthStart)
                    .SumAsync(pod => pod.Quantity * pod.UnitPrice),
                
                // Reviews
                TotalReviews = await _context.ServiceReviews.CountAsync(),
                AverageRating = await _context.ServiceReviews.AnyAsync() ? await _context.ServiceReviews.AverageAsync(r => r.OverallRating) : 0,
                PendingReviews = 0, // ServiceReview không có trường IsApproved
                
                UsersByRole = await GetUsersByRoleStatistics(),
                TopDevices = await GetTopDevicesStatistics(),
                RecentActivities = await GetRecentActivities(),
                
                // Top products
                TopSellingProducts = await GetTopSellingProducts(),
                
                // Recent orders
                RecentOrders = await GetRecentOrders(startDate, endDateTime),
                
                // Chart data - Truyền tham số lọc
                RevenueChartData = await GetRevenueChartData(startDate, endDate),
                OrderStatusChartData = GetOrderStatusChartData(allOrders),
                ProfitChartData = await GetProfitChartData(startDate, endDate),
                PaymentMethodChartData = await GetPaymentMethodChartData(startDate, endDateTime),
                CategoryRevenueChartData = await GetCategoryRevenueChartData(startDate, endDateTime),
                PeakHoursChartData = await GetPeakHoursChartData(startDate, endDateTime),
                OrderCompletionRateChartData = await GetOrderCompletionRateChartData(startDate, endDate),
                CustomerGrowthChartData = await GetCustomerGrowthChartData(startDate, endDate)
            };
        }

        private async Task<UserStatisticsViewModel> GetUserStatisticsData()
        {
            var today = DateTime.UtcNow.Date;

            return new UserStatisticsViewModel
            {
                TotalRegistrations = await _userManager.Users.CountAsync(),
                EmailConfirmedUsers = await _userManager.Users.CountAsync(u => u.EmailConfirmed),
                TwoFactorEnabledUsers = await _userManager.Users.CountAsync(u => u.TwoFactorEnabled),
                UserGrowthChart = new List<UserGrowthData>(),
                RoleDistribution = new List<RoleDistribution>(),
                TopActiveUsers = new List<UserActivitySummary>()
            };
        }

        private async Task<SystemHealthViewModel> GetSystemHealthData()
        {
            return new SystemHealthViewModel
            {
                DatabaseStatus = await CheckDatabaseHealth(),
                EmailServiceStatus = "Healthy",
                DatabaseSize = 1024 * 1024 * 100, // 100MB mock
                ActiveSessions = await _context.LoginHistories
                    .Where(l => l.LoginTime >= DateTime.UtcNow.AddHours(-24))
                    .Select(l => l.SessionId)
                    .Distinct()
                    .CountAsync(),
                CpuUsage = 45.5,
                MemoryUsage = 62.3,
                SystemAlerts = new List<SystemAlert>(),
                LastBackup = DateTime.UtcNow.AddDays(-1)
            };
        }

        private async Task<ReportsViewModel> GetReportsData()
        {
            return new ReportsViewModel
            {
                MonthlyUserReports = new List<MonthlyReport>(),
                SecurityReports = new List<SecurityReport>(),
                ActivityReports = new List<ActivityReport>()
            };
        }

        private async Task<Dictionary<string, int>> GetUsersByRoleStatistics()
        {
            var result = new Dictionary<string, int>();
            var roles = await _roleManager.Roles.ToListAsync();
            
            foreach (var role in roles)
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name);
                result[role.Name] = usersInRole.Count(u => !u.IsDeleted);
            }
            
            return result;
        }

        private async Task<List<DeviceStatistic>> GetTopDevicesStatistics()
        {
            return await _context.LoginHistories
                .Where(l => l.LoginTime >= DateTime.UtcNow.AddDays(-30))
                .GroupBy(l => l.UserAgent.Contains("Mobile") ? "Mobile" : 
                             l.UserAgent.Contains("Tablet") ? "Tablet" : "Desktop")
                .Select(g => new DeviceStatistic 
                { 
                    DeviceType = g.Key, 
                    Count = g.Count() 
                })
                .OrderByDescending(d => d.Count)
                .Take(5)
                .ToListAsync();
        }

        private async Task<List<RecentActivity>> GetRecentActivities()
        {
            var recentLogins = await _context.LoginHistories
                .Where(l => l.LoginTime >= DateTime.UtcNow.AddHours(-24))
                .OrderByDescending(l => l.LoginTime)
                .Take(10)
                .ToListAsync();

            var result = new List<RecentActivity>();
            foreach (var login in recentLogins)
            {
                var user = await _userManager.FindByIdAsync(login.UserId);
                if (user != null)
                {
                    result.Add(new RecentActivity
                    {
                        UserName = user.UserName ?? "Unknown",
                        Action = login.IsNewDevice ? "Đăng nhập từ thiết bị mới" : "Đăng nhập",
                        Timestamp = login.LoginTime,
                        IpAddress = login.IPAddress ?? "Unknown"
                    });
                }
            }
            return result;
        }

        private async Task<List<TopProductItem>> GetTopSellingProducts()
        {
            return await _context.OrderDetails
                .Where(od => od.Order.Status == "Hoàn thành" || od.Order.Status == "Đã giao")
                .GroupBy(od => new { od.ProductId, od.Product.Name, od.Product.ImageUrl })
                .Select(g => new TopProductItem
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.Name ?? "N/A",
                    ImageUrl = g.Key.ImageUrl,
                    TotalSold = g.Sum(od => od.Quantity),
                    TotalRevenue = g.Sum(od => od.UnitPrice * od.Quantity)
                })
                .OrderByDescending(p => p.TotalSold)
                .Take(5)
                .ToListAsync();
        }

        private async Task<List<RecentOrderItem>> GetRecentOrders(DateTime startDate, DateTime endDateTime)
        {
            return await _context.Orders
                .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDateTime)
                .OrderByDescending(o => o.OrderDate)
                .Take(10)
                .Select(o => new RecentOrderItem
                {
                    OrderId = o.OrderId ?? "N/A",
                    UserId = o.UserId ?? "N/A",
                    TotalAmount = o.TotalAmount,
                    Status = o.Status ?? "N/A",
                    OrderDate = o.OrderDate
                })
                .ToListAsync();
        }

        private async Task<List<ChartDataPoint>> GetRevenueChartData(DateTime startDate, DateTime endDate)
        {
            var result = new List<ChartDataPoint>();
            var completedStatuses = new[] { "Hoàn thành", "Đã giao" };
            
            // Tính số ngày giữa startDate và endDate
            var dayCount = (endDate - startDate).Days + 1;
            
            for (int i = 0; i < dayCount; i++)
            {
                var date = startDate.AddDays(i);
                var nextDate = date.AddDays(1);
                
                var revenue = await _context.Orders
                    .Where(o => o.OrderDate >= date && o.OrderDate < nextDate && completedStatuses.Contains(o.Status))
                    .SumAsync(o => o.TotalAmount);
                
                result.Add(new ChartDataPoint
                {
                    Label = date.ToString("dd/MM"),
                    Value = (double)revenue
                });
            }
            
            return result;
        }

        private List<ChartDataPoint> GetOrderStatusChartData(List<Order> allOrders)
        {
            return new List<ChartDataPoint>
            {
                new ChartDataPoint { Label = "Chờ xác nhận", Value = allOrders.Count(o => o.Status == "Chờ xác nhận") },
                new ChartDataPoint { Label = "Đã xác nhận", Value = allOrders.Count(o => o.Status == "Đã xác nhận") },
                new ChartDataPoint { Label = "Đang giao", Value = allOrders.Count(o => o.Status == "Đang giao") },
                new ChartDataPoint { Label = "Đã giao", Value = allOrders.Count(o => o.Status == "Đã giao") },
                new ChartDataPoint { Label = "Hoàn thành", Value = allOrders.Count(o => o.Status == "Hoàn thành") },
                new ChartDataPoint { Label = "Đã hủy", Value = allOrders.Count(o => o.Status == "Đã hủy") }
            };
        }

        private async Task<List<ProfitChartDataPoint>> GetProfitChartData(DateTime startDate, DateTime endDate)
        {
            var result = new List<ProfitChartDataPoint>();
            var dayCount = (endDate - startDate).Days + 1;

            for (int i = 0; i < dayCount; i++)
            {
                var date = startDate.AddDays(i);
                var nextDate = date.AddDays(1);

                // Doanh thu từ đơn hàng hoàn thành
                var revenue = await _context.Orders
                    .Where(o => (o.Status == "Hoàn thành" || o.Status == "Đã giao") 
                                && o.OrderDate >= date && o.OrderDate < nextDate)
                    .SumAsync(o => (double)o.TotalAmount);

                // Chi phí nhập hàng trong ngày
                var cost = await _context.PurchaseOrders
                    .Where(p => p.OrderDate >= date && p.OrderDate < nextDate)
                    .SumAsync(p => (double)p.TotalAmount);

                // Lợi nhuận = Doanh thu - Chi phí
                var profit = revenue - cost;

                result.Add(new ProfitChartDataPoint
                {
                    Label = date.ToString("dd/MM"),
                    Revenue = revenue,
                    Cost = cost,
                    Profit = profit
                });
            }

            return result;
        }

        private async Task<List<ChartDataPoint>> GetPaymentMethodChartData(DateTime startDate, DateTime endDateTime)
        {
            var completedStatuses = new[] { "Hoàn thành", "Đã giao" };
            
            var paymentMethods = await _context.Orders
                .Where(o => completedStatuses.Contains(o.Status) && o.OrderDate >= startDate && o.OrderDate <= endDateTime)
                .GroupBy(o => o.PaymentMethod ?? "Chưa xác định")
                .Select(g => new ChartDataPoint
                {
                    Label = g.Key,
                    Value = g.Count()
                })
                .OrderByDescending(x => x.Value)
                .ToListAsync();
            
            return paymentMethods;
        }

        private async Task<List<ChartDataPoint>> GetCategoryRevenueChartData(DateTime startDate, DateTime endDateTime)
        {
            var completedStatuses = new[] { "Hoàn thành", "Đã giao" };
            
            var categoryRevenue = await _context.OrderDetails
                .Where(od => completedStatuses.Contains(od.Order.Status) && od.Order.OrderDate >= startDate && od.Order.OrderDate <= endDateTime)
                .SelectMany(od => od.Product.ProductCategories.Select(pc => new 
                {
                    CategoryName = pc.Category.Name ?? "Khác",
                    Revenue = od.UnitPrice * od.Quantity
                }))
                .GroupBy(x => x.CategoryName)
                .Select(g => new ChartDataPoint
                {
                    Label = g.Key,
                    Value = (double)g.Sum(x => x.Revenue)
                })
                .OrderByDescending(x => x.Value)
                .Take(8)
                .ToListAsync();
            
            return categoryRevenue;
        }

        private async Task<List<ChartDataPoint>> GetPeakHoursChartData(DateTime startDate, DateTime endDateTime)
        {
            var hourlyOrders = await _context.Orders
                .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDateTime)
                .GroupBy(o => o.OrderDate.Hour)
                .Select(g => new ChartDataPoint
                {
                    Label = g.Key.ToString() + "h",
                    Value = g.Count()
                })
                .ToListAsync();
            
            // Đảm bảo có đủ 24 giờ (0-23)
            var result = new List<ChartDataPoint>();
            for (int hour = 0; hour < 24; hour++)
            {
                var existingData = hourlyOrders.FirstOrDefault(h => h.Label == hour + "h");
                result.Add(new ChartDataPoint
                {
                    Label = hour + "h",
                    Value = existingData?.Value ?? 0
                });
            }
            
            return result;
        }

        private async Task<List<ChartDataPoint>> GetOrderCompletionRateChartData(DateTime startDate, DateTime endDate)
        {
            var result = new List<ChartDataPoint>();
            var dayCount = (endDate - startDate).Days + 1;
            
            for (int i = 0; i < dayCount; i++)
            {
                var date = startDate.AddDays(i);
                var nextDate = date.AddDays(1);
                
                // Tổng số đơn hàng trong ngày
                var totalOrders = await _context.Orders
                    .CountAsync(o => o.OrderDate >= date && o.OrderDate < nextDate);
                
                // Số đơn hoàn thành
                var completedOrders = await _context.Orders
                    .CountAsync(o => (o.OrderDate >= date && o.OrderDate < nextDate) 
                                     && (o.Status == "Hoàn thành" || o.Status == "Đã giao"));
                
                // Tính tỷ lệ % (nếu không có đơn nào thì 0%)
                double completionRate = totalOrders > 0 
                    ? (double)completedOrders / totalOrders * 100 
                    : 0;
                
                result.Add(new ChartDataPoint
                {
                    Label = date.ToString("dd/MM"),
                    Value = Math.Round(completionRate, 1)
                });
            }
            
            return result;
        }

        private async Task<List<ChartDataPoint>> GetCustomerGrowthChartData(DateTime startDate, DateTime endDate)
        {
            var result = new List<ChartDataPoint>();
            var dayCount = (endDate - startDate).Days + 1;
            
            for (int i = 0; i < dayCount; i++)
            {
                var date = startDate.AddDays(i);
                var nextDate = date.AddDays(1);
                
                // Đếm số user mới đăng ký trong ngày
                var newUsers = await _userManager.Users
                    .CountAsync(u => u.CreatedAt >= date && u.CreatedAt < nextDate);
                
                result.Add(new ChartDataPoint
                {
                    Label = date.ToString("dd/MM"),
                    Value = newUsers
                });
            }
            
            return result;
        }

        private async Task<string> CheckDatabaseHealth()
        {
            try
            {
                await _context.Database.CanConnectAsync();
                return "Healthy";
            }
            catch
            {
                return "Unhealthy";
            }
        }
    }
}