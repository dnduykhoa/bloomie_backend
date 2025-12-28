using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminPurchaseOrderController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminPurchaseOrderController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Hiển thị danh sách phiếu nhập kho
        public IActionResult Index(string? searchString, int? supplierId, DateTime? fromDate, DateTime? toDate)
        {
            var query = _context.PurchaseOrders
                .Include(o => o.Supplier)
                .AsQueryable();

            // Áp dụng bộ lọc tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(o => 
                    (o.Note != null && o.Note.Contains(searchString))
                );
            }

            // Áp dụng bộ lọc nhà cung cấp
            if (supplierId.HasValue)
            {
                query = query.Where(o => o.SupplierId == supplierId.Value);
            }

            // Áp dụng bộ lọc theo ngày
            if (fromDate.HasValue)
            {
                query = query.Where(o => o.OrderDate.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(o => o.OrderDate.Date <= toDate.Value.Date);
            }

            var orders = query.OrderByDescending(x => x.OrderDate).ToList();

            // Tính toán thống kê
            var allOrders = _context.PurchaseOrders.ToList();
            ViewBag.TotalOrders = allOrders.Count;
            ViewBag.OrdersThisMonth = allOrders.Count(o => 
                o.OrderDate.Month == DateTime.Now.Month && 
                o.OrderDate.Year == DateTime.Now.Year
            );
            ViewBag.TotalSuppliers = _context.Suppliers.Count();

            // Truyền giá trị bộ lọc và danh sách nhà cung cấp
            ViewBag.SearchString = searchString;
            ViewBag.SupplierId = supplierId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.Suppliers = _context.Suppliers.ToList();

            return View(orders);
        }

        // Hiển thị form tạo phiếu nhập kho
        public IActionResult Create()
        {
            ViewBag.Suppliers = _context.Suppliers.ToList();
            ViewBag.FlowerTypes = _context.FlowerTypes.ToList();
            // FlowerVariants sẽ được load động theo FlowerType (AJAX)
            return View();
        }

        // Xử lý lưu phiếu nhập kho
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(PurchaseOrderViewModel model)
        {
            // Basic server-side validation
            if (model == null)
            {
                ModelState.AddModelError(string.Empty, "Dữ liệu phiếu nhập không hợp lệ.");
            }

            if (model.Details == null || !model.Details.Any())
            {
                ModelState.AddModelError(string.Empty, "Vui lòng thêm ít nhất một dòng chi tiết nhập kho.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Suppliers = _context.Suppliers.ToList();
                ViewBag.FlowerTypes = _context.FlowerTypes.ToList();
                return View(model);
            }

            var order = new PurchaseOrder
            {
                SupplierId = model.SupplierId,
                OrderDate = model.OrderDate,
                Note = model.Note,
                Details = new System.Collections.Generic.List<PurchaseOrderDetail>()
            };

            decimal totalAmount = 0;

            foreach (var d in model.Details)
            {
                // Validate each detail
                if (d.FlowerVariantId <= 0)
                {
                    ModelState.AddModelError(string.Empty, "Vui lòng chọn biến thể hoa cho mỗi dòng.");
                    ViewBag.Suppliers = _context.Suppliers.ToList();
                    ViewBag.FlowerTypes = _context.FlowerTypes.ToList();
                    return View(model);
                }
                if (d.Quantity <= 0)
                {
                    ModelState.AddModelError(string.Empty, "Số lượng phải lớn hơn 0.");
                    ViewBag.Suppliers = _context.Suppliers.ToList();
                    ViewBag.FlowerTypes = _context.FlowerTypes.ToList();
                    return View(model);
                }
                var detail = new PurchaseOrderDetail
                {
                    FlowerVariantId = d.FlowerVariantId,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice
                };
                order.Details.Add(detail);

                // Cộng vào tổng tiền
                totalAmount += d.Quantity * d.UnitPrice;

                // Cập nhật tồn kho cho FlowerVariant
                var variant = _context.FlowerVariants.Find(d.FlowerVariantId);
                if (variant != null)
                {
                    variant.Stock += d.Quantity;
                }
            }

            // Gán tổng tiền cho PurchaseOrder
            order.TotalAmount = totalAmount;

            _context.PurchaseOrders.Add(order);
            _context.SaveChanges();

            return RedirectToAction("Index");
        }
        
        // Hiển thị chi tiết phiếu nhập kho
        public IActionResult Details(int id)
        {
            var order = _context.PurchaseOrders
                .Include(o => o.Supplier)
                .Include(o => o.Details)
                    .ThenInclude(d => d.FlowerVariant)
                        .ThenInclude(v => v.FlowerType)
                .FirstOrDefault(o => o.Id == id);
            if (order == null)
            {
                return NotFound();
            }
            var model = new
            {
                Supplier = order.Supplier?.Name,
                OrderDate = order.OrderDate,
                Note = order.Note,
                Details = order.Details.Select(d => new
                {
                    Name = d.FlowerVariant?.Name,
                    FlowerType = d.FlowerVariant?.FlowerType?.Name,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice
                }).ToList()
            };
            return View(model);
        }
    }
}
