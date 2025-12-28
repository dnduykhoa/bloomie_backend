using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminRevenueController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminRevenueController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, string? filter)
        {
            // Mặc định lấy dữ liệu cả tháng hiện tại (từ ngày 1 đến cuối tháng)
            var now = DateTime.Now;
            var end = endDate?.Date.AddDays(1).AddTicks(-1) ?? new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59);
            var start = startDate?.Date ?? new DateTime(now.Year, now.Month, 1);

            // Áp dụng filter nhanh (chỉ khi user chọn)
            if (!string.IsNullOrEmpty(filter))
            {
                switch (filter)
                {
                    case "today":
                        start = DateTime.Now.Date;
                        end = DateTime.Now.Date.AddDays(1).AddTicks(-1);
                        break;
                    case "week":
                        start = DateTime.Now.AddDays(-7).Date;
                        end = DateTime.Now.Date.AddDays(1).AddTicks(-1);
                        break;
                    case "month":
                        start = DateTime.Now.AddDays(-30).Date;
                        end = DateTime.Now.Date.AddDays(1).AddTicks(-1);
                        break;
                    case "year":
                        start = DateTime.Now.AddYears(-1).Date;
                        end = DateTime.Now.Date.AddDays(1).AddTicks(-1);
                        break;
                }
            }

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.Date.ToString("yyyy-MM-dd"); // Format date only
            ViewBag.Filter = filter;

            // Lấy danh sách đơn hàng (bao gồm cả đơn đang xử lý, không tính đơn hủy)
            var orders = await _context.Orders
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                .Where(o => o.OrderDate >= start && o.OrderDate <= end && o.Status != "Đã hủy")
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            // Tính tổng doanh thu gốc
            var totalRevenue = orders.Sum(o => o.TotalAmount);

            // Tính tổng giá trị đơn hàng bị hoàn trả
            var returnedOrders = await _context.OrderReturns
                .Include(or => or.Order)
                .Where(or => or.Order.OrderDate >= start && or.Order.OrderDate <= end && or.Status == "Approved")
                .ToListAsync();
            var totalReturned = returnedOrders.Sum(or => or.RefundAmount ?? 0);

            // Tính tổng giá trị khuyến mãi/voucher/shipping đã sử dụng từ các đơn hàng
            var totalPromotionDiscount = orders.Sum(o => o.PromotionDiscount + o.VoucherDiscount + o.ShippingDiscount);
            
            // Tính từng loại giảm giá riêng
            // PromotionDiscount từ Order (nếu có lưu)
            var promotionDiscountOnly = orders.Sum(o => o.PromotionDiscount);
            
            // Tính thêm discount từ ProductDiscount (giá gốc - giá bán)
            var productDiscountFromDetails = orders
                .Where(o => o.OrderDetails != null)
                .SelectMany(o => o.OrderDetails!)
                .Sum(od => {
                    var originalPrice = od.Product?.Price ?? od.UnitPrice; // Giá gốc sản phẩm
                    var soldPrice = od.UnitPrice; // Giá đã bán
                    var discountPerUnit = originalPrice - soldPrice;
                    return discountPerUnit * od.Quantity;
                });
            
            // Tổng PromotionDiscount thực tế = từ Order + từ ProductDiscount
            promotionDiscountOnly = promotionDiscountOnly + productDiscountFromDetails;
            
            var voucherDiscountOnly = orders.Sum(o => o.VoucherDiscount);
            var shippingDiscountOnly = orders.Sum(o => o.ShippingDiscount);
            
            // Tính tổng giá trị điểm thành viên đã sử dụng
            var totalPointsValue = orders.Sum(o => o.PointsDiscount);

            // Doanh thu thực = Doanh thu - Hoàn trả
            var actualRevenue = totalRevenue - totalReturned;

            // Tính tổng chi phí nhập hàng (từ PurchaseOrders)
            var purchaseOrders = await _context.PurchaseOrders
                .Where(po => po.OrderDate >= start && po.OrderDate <= end)
                .ToListAsync();

            var totalCost = purchaseOrders.Sum(po => po.TotalAmount);

            // Tính lợi nhuận gộp (dùng doanh thu thực)
            var grossProfit = actualRevenue - totalCost;
            var profitMargin = actualRevenue > 0 ? (grossProfit / actualRevenue) * 100 : 0;

            // Thống kê theo phương thức thanh toán
            var revenueByPaymentMethod = orders
                .GroupBy(o => o.PaymentMethod ?? "Chưa xác định")
                .Select(g => new RevenueByPaymentMethodViewModel
                {
                    PaymentMethod = g.Key,
                    OrderCount = g.Count(),
                    Revenue = g.Sum(o => o.TotalAmount)
                })
                .OrderByDescending(r => r.Revenue)
                .ToList();

            // Thống kê theo trạng thái đơn hàng (tất cả trạng thái)
            var ordersByStatus = await _context.Orders
                .Where(o => o.OrderDate >= start && o.OrderDate <= end)
                .GroupBy(o => o.Status ?? "Chưa xác định")
                .Select(g => new OrderByStatusViewModel
                {
                    Status = g.Key,
                    OrderCount = g.Count(),
                    TotalAmount = g.Sum(o => o.TotalAmount)
                })
                .ToListAsync();

            // So sánh với kỳ trước
            var previousPeriodDays = (int)(end - start).TotalDays;
            var previousStart = start.AddDays(-previousPeriodDays - 1);
            var previousEnd = start.AddDays(-1);

            var previousOrders = await _context.Orders
                .Where(o => o.OrderDate >= previousStart && o.OrderDate <= previousEnd && o.Status == "Hoàn thành")
                .ToListAsync();
            var previousRevenue = previousOrders.Sum(o => o.TotalAmount);
            
            var revenueGrowth = previousRevenue > 0 
                ? ((actualRevenue - previousRevenue) / previousRevenue) * 100 
                : 0;

            // Doanh thu theo ngày
            var revenueByDate = orders
                .GroupBy(o => o.OrderDate.Date)
                .Select(g => new RevenueByDateViewModel
                {
                    Date = g.Key,
                    Revenue = g.Sum(o => o.TotalAmount),
                    OrderCount = g.Count()
                })
                .OrderBy(r => r.Date)
                .ToList();

            // Doanh thu theo sản phẩm
            var revenueByProduct = orders
                .SelectMany(o => o.OrderDetails)
                .GroupBy(od => new { od.ProductId, od.Product.Name })
                .Select(g => new RevenueByProductViewModel
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.Name ?? "N/A",
                    Quantity = g.Sum(od => od.Quantity),
                    Revenue = g.Sum(od => od.UnitPrice * od.Quantity)
                })
                .OrderByDescending(r => r.Revenue)
                .Take(10)
                .ToList();

            // Doanh thu theo danh mục
            var orderDetailsWithCategories = await _context.OrderDetails
                .Include(od => od.Order)
                .Include(od => od.Product)
                    .ThenInclude(p => p!.ProductCategories!)
                        .ThenInclude(pc => pc.Category)
                .Where(od => od.Order.OrderDate >= start && od.Order.OrderDate <= end && od.Order.Status == "Hoàn thành")
                .ToListAsync();

            var revenueByCategory = orderDetailsWithCategories
                .Where(od => od.Product?.ProductCategories != null)
                .SelectMany(od => od.Product!.ProductCategories!.Select(pc => new
                {
                    CategoryName = pc.Category?.Name ?? "N/A",
                    Revenue = od.UnitPrice * od.Quantity
                }))
                .GroupBy(x => x.CategoryName)
                .Select(g => new RevenueByCategoryViewModel
                {
                    CategoryName = g.Key,
                    Revenue = g.Sum(x => x.Revenue)
                })
                .OrderByDescending(r => r.Revenue)
                .ToList();

            // Top khách hàng
            var topCustomers = orders
                .GroupBy(o => new { o.UserId })
                .Select(g => new TopCustomerViewModel
                {
                    UserId = g.Key.UserId ?? "Guest",
                    OrderCount = g.Count(),
                    TotalSpent = g.Sum(o => o.TotalAmount)
                })
                .OrderByDescending(c => c.TotalSpent)
                .Take(10)
                .ToList();

            var viewModel = new RevenueViewModel
            {
                StartDate = start,
                EndDate = end,
                TotalRevenue = totalRevenue,
                ActualRevenue = actualRevenue,
                TotalReturned = totalReturned,
                TotalPromotionDiscount = totalPromotionDiscount,
                TotalPointsValue = totalPointsValue,
                TotalCost = totalCost,
                GrossProfit = grossProfit,
                ProfitMargin = profitMargin,
                TotalOrders = orders.Count,
                AverageOrderValue = orders.Count > 0 ? totalRevenue / orders.Count : 0,
                PreviousRevenue = previousRevenue,
                RevenueGrowth = revenueGrowth,
                RevenueByDate = revenueByDate,
                RevenueByProduct = revenueByProduct,
                RevenueByCategory = revenueByCategory,
                RevenueByPaymentMethod = revenueByPaymentMethod,
                OrdersByStatus = ordersByStatus,
                TopCustomers = topCustomers
            };

            // Truyền thêm chi tiết breakdown vào ViewBag
            ViewBag.PromotionDiscountOnly = promotionDiscountOnly;
            ViewBag.VoucherDiscountOnly = voucherDiscountOnly;
            ViewBag.ShippingDiscountOnly = shippingDiscountOnly;
            ViewBag.ReturnedOrdersCount = returnedOrders.Count;
            ViewBag.PurchaseOrdersCount = purchaseOrders.Count;

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(DateTime? startDate, DateTime? endDate)
        {
            var end = endDate ?? DateTime.Now.Date;
            var start = startDate ?? end.AddDays(-30);

            var orders = await _context.Orders
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                .Where(o => o.OrderDate >= start && o.OrderDate <= end && o.Status == "Hoàn thành")
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            // Tạo CSV content
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Mã đơn hàng,Ngày đặt,Khách hàng,Tổng tiền,Trạng thái");
            
            foreach (var order in orders)
            {
                csv.AppendLine($"{order.Id},{order.OrderDate:dd/MM/yyyy},{order.UserId},{order.TotalAmount:N0},{order.Status}");
            }

            csv.AppendLine();
            csv.AppendLine("Tổng doanh thu:," + orders.Sum(o => o.TotalAmount).ToString("N0"));

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"BaoCaoDoanhThu_{start:yyyyMMdd}_{end:yyyyMMdd}.csv");
        }
    }
}
