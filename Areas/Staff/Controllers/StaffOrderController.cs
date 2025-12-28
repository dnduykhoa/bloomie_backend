using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Services.Interfaces;

namespace Bloomie.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff")]
    public class StaffOrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;

        public StaffOrderController(ApplicationDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // Hiển thị danh sách đơn hàng
        [HttpGet]
        public async Task<IActionResult> Index(int pageNumber = 1, string? searchString = null, string? statusFilter = null)
        {
            int pageSize = 10;
            var query = _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(d => d!.Product)
                .AsQueryable();

            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(o => o.Status == statusFilter);
            }

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(o => (o.OrderId != null && o.OrderId.Contains(searchString)) ||
                                         (o.UserId != null && o.UserId.Contains(searchString)) ||
                                         (o.ShippingAddress != null && o.ShippingAddress.Contains(searchString)));
            }

            query = query.OrderByDescending(o => o.OrderDate);

            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var orders = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["TotalItems"] = totalItems;
            ViewData["PageSize"] = pageSize;
            ViewData["SearchString"] = searchString;
            ViewData["StatusFilter"] = statusFilter;

            return View(orders);
        }

        // Xem chi tiết đơn hàng
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(d => d!.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound();

            return View(order);
        }

        // POST: Xác nhận đơn hàng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmOrder(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails!)
                    .ThenInclude(od => od!.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            if (order.Status != "Chờ xác nhận")
            {
                TempData["error"] = "Không thể xác nhận đơn hàng ở trạng thái hiện tại.";
                return RedirectToAction("Details", new { id });
            }

            // Kiểm tra và trừ tồn kho
            if (order.OrderDetails != null)
            {
                foreach (var detail in order.OrderDetails)
                {
                    var product = detail.Product;
                    if (product == null) continue;

                    if (product.StockQuantity < detail.Quantity)
                    {
                        TempData["error"] = $"Sản phẩm '{product.Name}' không đủ số lượng (Còn: {product.StockQuantity}, Cần: {detail.Quantity}).";
                        return RedirectToAction("Details", new { id });
                    }

                    product.StockQuantity -= detail.Quantity;

                    // Trừ kho nguyên liệu
                    var productDetails = await _context.ProductDetails
                        .Where(pd => pd.ProductId == product.Id)
                        .ToListAsync();

                    foreach (var pd in productDetails)
                    {
                        var flowerVariant = await _context.FlowerVariants.FindAsync(pd.FlowerVariantId);
                        if (flowerVariant != null)
                        {
                            int requiredQuantity = pd.Quantity * detail.Quantity;
                            if (flowerVariant.Stock < requiredQuantity)
                            {
                                TempData["error"] = $"Nguyên liệu '{flowerVariant.Name}' không đủ (Còn: {flowerVariant.Stock}, Cần: {requiredQuantity}).";
                                return RedirectToAction("Details", new { id });
                            }
                            flowerVariant.Stock -= requiredQuantity;
                        }
                    }
                }
            }

            order.Status = "Đã xác nhận";
            await _context.SaveChangesAsync();

            TempData["success"] = "Đã xác nhận đơn hàng thành công.";
            return RedirectToAction("Details", new { id });
        }

        // POST: Cập nhật trạng thái đơn hàng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, string newStatus)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index");
            }

            order.Status = newStatus;
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã cập nhật trạng thái đơn hàng thành '{newStatus}'.";
            return RedirectToAction("Details", new { id });
        }
    }
}
