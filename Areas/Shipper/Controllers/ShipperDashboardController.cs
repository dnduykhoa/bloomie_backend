using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.ViewModels;
using System.Security.Claims;

namespace Bloomie.Areas.Shipper.Controllers
{
    [Area("Shipper")]
    [Authorize(Roles = "Shipper")]
    public class ShipperDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ShipperDashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Shipper/ShipperDashboard - Dashboard tổng quan
        public async Task<IActionResult> Index()
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            // Tất cả đơn của shipper này
            var allOrders = await _context.Orders
                .Where(o => o.ShipperId == currentUserId)
                .ToListAsync();

            // Đơn hôm nay
            var todayOrders = allOrders.Where(o => o.OrderDate.Date == today).ToList();
            
            // Đơn tuần này
            var weekOrders = allOrders.Where(o => o.OrderDate >= startOfWeek).ToList();
            
            // Đơn tháng này
            var monthOrders = allOrders.Where(o => o.OrderDate >= startOfMonth).ToList();

            var viewModel = new ShipperDashboardViewModel
            {
                // Hôm nay
                TotalOrders = todayOrders.Count,
                CompletedOrders = todayOrders.Count(o => o.Status == "Đã giao" || o.Status == "Hoàn thành"),
                InProgressOrders = todayOrders.Count(o => o.Status == "Đang giao"),
                FailedOrders = todayOrders.Count(o => o.Status == "Đã xác nhận" && !string.IsNullOrEmpty(o.Note) && o.Note.Contains("Giao hàng thất bại")),
                
                // Tuần này
                WeekTotalOrders = weekOrders.Count,
                WeekCompletedOrders = weekOrders.Count(o => o.Status == "Đã giao" || o.Status == "Hoàn thành"),
                
                // Tháng này
                MonthTotalOrders = monthOrders.Count,
                MonthCompletedOrders = monthOrders.Count(o => o.Status == "Đã giao" || o.Status == "Hoàn thành"),
                
                // Tiền COD đã thu
                CodCollectedToday = todayOrders
                    .Where(o => o.PaymentMethod == "COD" 
                        && (o.Status == "Đã giao" || o.Status == "Hoàn thành")
                        && o.PaymentStatus == "Đã thanh toán")
                    .Sum(o => o.TotalAmount),
                
                CodCollectedWeek = weekOrders
                    .Where(o => o.PaymentMethod == "COD" 
                        && (o.Status == "Đã giao" || o.Status == "Hoàn thành")
                        && o.PaymentStatus == "Đã thanh toán")
                    .Sum(o => o.TotalAmount),
                
                CodCollectedMonth = monthOrders
                    .Where(o => o.PaymentMethod == "COD" 
                        && (o.Status == "Đã giao" || o.Status == "Hoàn thành")
                        && o.PaymentStatus == "Đã thanh toán")
                    .Sum(o => o.TotalAmount),
                
                // Danh sách đơn COD hôm nay
                CodOrders = todayOrders
                    .Where(o => o.PaymentMethod == "COD" && (o.Status == "Đã giao" || o.Status == "Hoàn thành"))
                    .OrderByDescending(o => o.OrderDate)
                    .ToList()
            };

            return View(viewModel);
        }

        // GET: Shipper/ShipperDashboard/CODSummary - Chi tiết tiền COD
        public async Task<IActionResult> CODSummary(DateTime? date)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var selectedDate = date ?? DateTime.Today;

            var codOrders = await _context.Orders
                .Where(o => o.ShipperId == currentUserId
                    && o.PaymentMethod == "COD"
                    && (o.Status == "Đã giao" || o.Status == "Hoàn thành")
                    && o.OrderDate.Date == selectedDate.Date)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            ViewBag.SelectedDate = selectedDate.ToString("yyyy-MM-dd");
            ViewBag.TotalAmount = codOrders.Sum(o => o.TotalAmount);

            return View(codOrders);
        }
    }
}
