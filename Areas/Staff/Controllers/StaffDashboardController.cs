using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Areas.Staff.Models;

namespace Bloomie.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff")]
    public class StaffDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StaffDashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Đếm số lượng đơn hàng đang chờ
            var soLuongDonHangCho = await _context.Orders
                .CountAsync(o => o.Status == "Pending" || o.Status == "Chờ xác nhận");

            // Danh sách 5 đơn hàng gần đây
            var donHangGanDay = await _context.Orders
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .Select(o => new OrderSummary
                {
                    Id = o.OrderId ?? "",
                    TenNguoiDung = o.UserId ?? "N/A",
                    OrderDate = o.OrderDate,
                    TotalPrice = o.TotalAmount,
                    OrderStatus = o.Status ?? ""
                })
                .ToListAsync();

            // Danh sách 5 đánh giá gần đây
            var danhGiaGanDay = await _context.Ratings
                .Include(r => r.User)
                .Include(r => r.Product)
                .OrderByDescending(r => r.ReviewDate)
                .Take(5)
                .Select(r => new RatingSummary
                {
                    Id = r.Id,
                    TenNguoiDung = r.User != null ? r.User.FullName : "N/A",
                    TenSanPham = r.Product != null ? r.Product.Name : "N/A",
                    GiaTriDanhGia = r.Star,
                    ReviewDate = r.ReviewDate,
                    IsVisible = r.IsVisible
                })
                .ToListAsync();

            // Đếm số lượng báo cáo chưa giải quyết
            var soLuongBaoCaoChuaGiaiQuyet = await _context.Reports
                .CountAsync(r => !r.IsResolved);

            // Truyền dữ liệu vào view
            ViewData["SoLuongDonHangCho"] = soLuongDonHangCho;
            ViewData["DonHangGanDay"] = donHangGanDay;
            ViewData["DanhGiaGanDay"] = danhGiaGanDay;
            ViewData["SoLuongBaoCaoChuaGiaiQuyet"] = soLuongBaoCaoChuaGiaiQuyet;

            return View();
        }
    }
}