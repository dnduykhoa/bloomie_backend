using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminShippingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminShippingController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Admin/AdminShipping
        public async Task<IActionResult> Index(string searchString, string statusFilter, decimal? minFee, decimal? maxFee)
        {
            // Calculate statistics
            var totalShipping = await _context.ShippingFees.CountAsync();
            var activeShipping = await _context.ShippingFees.CountAsync(sf => sf.IsActive);
            var inactiveShipping = await _context.ShippingFees.CountAsync(sf => !sf.IsActive);
            var avgFee = await _context.ShippingFees.AnyAsync() 
                ? await _context.ShippingFees.AverageAsync(sf => sf.Fee) 
                : 0;

            ViewBag.TotalShipping = totalShipping;
            ViewBag.ActiveShipping = activeShipping;
            ViewBag.InactiveShipping = inactiveShipping;
            ViewBag.AverageFee = avgFee;
            
            // Apply filters
            var query = _context.ShippingFees.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                query = query.Where(sf => sf.WardName.Contains(searchString));
                ViewBag.SearchString = searchString;
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                bool isActive = statusFilter == "true";
                query = query.Where(sf => sf.IsActive == isActive);
                ViewBag.StatusFilter = statusFilter;
            }

            if (minFee.HasValue)
            {
                query = query.Where(sf => sf.Fee >= minFee.Value);
                ViewBag.MinFee = minFee.Value;
            }

            if (maxFee.HasValue)
            {
                query = query.Where(sf => sf.Fee <= maxFee.Value);
                ViewBag.MaxFee = maxFee.Value;
            }

            var shippingFees = await query
                .OrderBy(sf => sf.WardName)
                .ToListAsync();
            
            return View(shippingFees);
        }

        // GET: Admin/AdminShipping/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admin/AdminShipping/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ShippingFee shippingFee)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra phường/xã đã tồn tại chưa
                var existing = await _context.ShippingFees
                    .FirstOrDefaultAsync(sf => sf.WardCode == shippingFee.WardCode);
                
                if (existing != null)
                {
                    TempData["error"] = $"Phường/xã {shippingFee.WardName} đã tồn tại trong hệ thống!";
                    return View(shippingFee);
                }

                shippingFee.CreatedAt = DateTime.UtcNow;
                _context.ShippingFees.Add(shippingFee);
                await _context.SaveChangesAsync();
                
                TempData["success"] = $"Thêm phí ship cho phường/xã {shippingFee.WardName} thành công!";
                return RedirectToAction(nameof(Index));
            }
            
            return View(shippingFee);
        }

        // GET: Admin/AdminShipping/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var shippingFee = await _context.ShippingFees.FindAsync(id);
            if (shippingFee == null)
                return NotFound();

            return View(shippingFee);
        }

        // POST: Admin/AdminShipping/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ShippingFee shippingFee)
        {
            if (id != shippingFee.Id)
                return NotFound();

            if (!ModelState.IsValid)
                return View(shippingFee);

            try
            {
                shippingFee.UpdatedAt = DateTime.UtcNow;
                _context.Update(shippingFee);
                await _context.SaveChangesAsync();
                
                TempData["success"] = "Cập nhật phí ship thành công!";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ShippingFeeExists(shippingFee.Id))
                    return NotFound();
                else
                    throw;
            }
        }

        // POST: Admin/AdminShipping/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var shippingFee = await _context.ShippingFees.FindAsync(id);
            if (shippingFee != null)
            {
                _context.ShippingFees.Remove(shippingFee);
                await _context.SaveChangesAsync();
                TempData["success"] = $"Xóa phí ship phường/xã {shippingFee.WardName} thành công!";
            }
            else
            {
                TempData["error"] = "Không tìm thấy phí ship!";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Admin/AdminShipping/ToggleActive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var shippingFee = await _context.ShippingFees.FindAsync(id);
            if (shippingFee == null)
                return Json(new { success = false, message = "Không tìm thấy phí ship!" });

            shippingFee.IsActive = !shippingFee.IsActive;
            shippingFee.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                isActive = shippingFee.IsActive,
                message = shippingFee.IsActive ? "Đã kích hoạt" : "Đã tắt"
            });
        }

        private bool ShippingFeeExists(int id)
        {
            return _context.ShippingFees.Any(e => e.Id == id);
        }
    }
}
