using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using System.Text.Json;

namespace Bloomie.Areas.Manager.Controllers
{
    [Area("Manager")]
    [Authorize(Roles = "Manager")]
    public class ManagerProductDiscountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ManagerProductDiscountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Manager/ManagerProductDiscount
        public async Task<IActionResult> Index()
        {
            var discounts = await _context.ProductDiscounts
                .OrderByDescending(pd => pd.CreatedDate)
                .ToListAsync();

            return View(discounts);
        }

        // GET: Manager/ManagerProductDiscount/Create
        public async Task<IActionResult> Create()
        {
            var viewModel = new ProductDiscountVM
            {
                AvailableProducts = await _context.Products
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Name)
                    .ToListAsync(),
                AvailableCategories = await _context.Categories
                    .OrderBy(c => c.Name)
                    .ToListAsync(),
                StartDate = DateTime.Now,
                Priority = 1,
                IsActive = true
            };

            return View(viewModel);
        }

        // POST: Manager/ManagerProductDiscount/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductDiscountVM model)
        {
            if (ModelState.IsValid)
            {
                // Validation logic
                if (model.EndDate.HasValue && model.StartDate.HasValue && model.EndDate < model.StartDate)
                {
                    ModelState.AddModelError("EndDate", "Ngày kết thúc phải sau ngày bắt đầu");
                }

                if (model.DiscountType == "percent" && model.DiscountValue > 100)
                {
                    ModelState.AddModelError("DiscountValue", "Phần trăm giảm không được vượt quá 100%");
                }

                if (model.ApplyTo == "products" && (model.SelectedProductIds == null || !model.SelectedProductIds.Any()))
                {
                    ModelState.AddModelError("SelectedProductIds", "Vui lòng chọn ít nhất 1 sản phẩm");
                }

                if (model.ApplyTo == "categories" && (model.SelectedCategoryIds == null || !model.SelectedCategoryIds.Any()))
                {
                    ModelState.AddModelError("SelectedCategoryIds", "Vui lòng chọn ít nhất 1 danh mục");
                }

                if (ModelState.IsValid)
                {
                    var discount = new ProductDiscount
                    {
                        Name = model.Name,
                        Description = model.Description,
                        DiscountType = model.DiscountType,
                        DiscountValue = model.DiscountValue,
                        MaxDiscount = model.MaxDiscount,
                        StartDate = model.StartDate ?? DateTime.Now,
                        EndDate = model.EndDate,
                        ApplyTo = model.ApplyTo,
                        ProductIds = model.ApplyTo == "products" && model.SelectedProductIds != null
                            ? JsonSerializer.Serialize(model.SelectedProductIds)
                            : null,
                        CategoryIds = model.ApplyTo == "categories" && model.SelectedCategoryIds != null
                            ? JsonSerializer.Serialize(model.SelectedCategoryIds)
                            : null,
                        Priority = model.Priority,
                        IsActive = model.IsActive,
                        CreatedDate = DateTime.Now,
                        UpdatedDate = DateTime.Now,
                        ViewCount = 0,
                        PurchaseCount = 0,
                        TotalRevenue = 0
                    };

                    _context.ProductDiscounts.Add(discount);
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "✅ Tạo chương trình giảm giá thành công!";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Reload data nếu có lỗi
            model.AvailableProducts = await _context.Products
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();
            model.AvailableCategories = await _context.Categories
                .OrderBy(c => c.Name)
                .ToListAsync();

            return View(model);
        }

        // GET: Manager/ManagerProductDiscount/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var discount = await _context.ProductDiscounts.FindAsync(id);
            if (discount == null)
            {
                return NotFound();
            }

            var viewModel = new ProductDiscountVM
            {
                Id = discount.Id,
                Name = discount.Name,
                Description = discount.Description,
                DiscountType = discount.DiscountType,
                DiscountValue = discount.DiscountValue,
                MaxDiscount = discount.MaxDiscount,
                StartDate = discount.StartDate,
                EndDate = discount.EndDate,
                ApplyTo = discount.ApplyTo,
                SelectedProductIds = !string.IsNullOrEmpty(discount.ProductIds)
                    ? JsonSerializer.Deserialize<List<int>>(discount.ProductIds)
                    : null,
                SelectedCategoryIds = !string.IsNullOrEmpty(discount.CategoryIds)
                    ? JsonSerializer.Deserialize<List<int>>(discount.CategoryIds)
                    : null,
                Priority = discount.Priority,
                IsActive = discount.IsActive,
                AvailableProducts = await _context.Products
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Name)
                    .ToListAsync(),
                AvailableCategories = await _context.Categories
                    .OrderBy(c => c.Name)
                    .ToListAsync()
            };

            return View(viewModel);
        }

        // POST: Manager/ManagerProductDiscount/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductDiscountVM model)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                // Validation
                if (model.EndDate.HasValue && model.StartDate.HasValue && model.EndDate < model.StartDate)
                {
                    ModelState.AddModelError("EndDate", "Ngày kết thúc phải sau ngày bắt đầu");
                }

                if (model.DiscountType == "percent" && model.DiscountValue > 100)
                {
                    ModelState.AddModelError("DiscountValue", "Phần trăm giảm không được vượt quá 100%");
                }

                if (model.ApplyTo == "products" && (model.SelectedProductIds == null || !model.SelectedProductIds.Any()))
                {
                    ModelState.AddModelError("SelectedProductIds", "Vui lòng chọn ít nhất 1 sản phẩm");
                }

                if (model.ApplyTo == "categories" && (model.SelectedCategoryIds == null || !model.SelectedCategoryIds.Any()))
                {
                    ModelState.AddModelError("SelectedCategoryIds", "Vui lòng chọn ít nhất 1 danh mục");
                }

                if (ModelState.IsValid)
                {
                    try
                    {
                        var discount = await _context.ProductDiscounts.FindAsync(id);
                        if (discount == null)
                        {
                            return NotFound();
                        }

                        discount.Name = model.Name;
                        discount.Description = model.Description;
                        discount.DiscountType = model.DiscountType;
                        discount.DiscountValue = model.DiscountValue;
                        discount.MaxDiscount = model.MaxDiscount;
                        discount.StartDate = model.StartDate ?? DateTime.Now;
                        discount.EndDate = model.EndDate;
                        discount.ApplyTo = model.ApplyTo;
                        discount.ProductIds = model.ApplyTo == "products" && model.SelectedProductIds != null
                            ? JsonSerializer.Serialize(model.SelectedProductIds)
                            : null;
                        discount.CategoryIds = model.ApplyTo == "categories" && model.SelectedCategoryIds != null
                            ? JsonSerializer.Serialize(model.SelectedCategoryIds)
                            : null;
                        discount.Priority = model.Priority;
                        discount.IsActive = model.IsActive;
                        discount.UpdatedDate = DateTime.Now;

                        _context.Update(discount);
                        await _context.SaveChangesAsync();

                        TempData["SuccessMessage"] = "✅ Cập nhật chương trình giảm giá thành công!";
                        return RedirectToAction(nameof(Index));
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        if (!ProductDiscountExists(model.Id))
                        {
                            return NotFound();
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }

            // Reload data
            model.AvailableProducts = await _context.Products
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();
            model.AvailableCategories = await _context.Categories
                .OrderBy(c => c.Name)
                .ToListAsync();

            return View(model);
        }

        // POST: Manager/ManagerProductDiscount/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var discount = await _context.ProductDiscounts.FindAsync(id);
            if (discount == null)
            {
                return NotFound();
            }

            _context.ProductDiscounts.Remove(discount);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "✅ Xóa chương trình giảm giá thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: Manager/ManagerProductDiscount/ToggleActive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var discount = await _context.ProductDiscounts.FindAsync(id);
            if (discount == null)
            {
                return NotFound();
            }

            discount.IsActive = !discount.IsActive;
            discount.UpdatedDate = DateTime.Now;

            _context.Update(discount);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = discount.IsActive 
                ? "✅ Đã kích hoạt chương trình giảm giá!" 
                : "⏸️ Đã tạm dừng chương trình giảm giá!";

            return RedirectToAction(nameof(Index));
        }

        private bool ProductDiscountExists(int id)
        {
            return _context.ProductDiscounts.Any(e => e.Id == id);
        }
    }
}
