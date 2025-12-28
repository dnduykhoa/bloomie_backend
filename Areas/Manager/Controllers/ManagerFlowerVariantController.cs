using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Linq;

namespace Bloomie.Areas.Manager.Controllers
{
	[Area("Manager")]
	[Authorize(Roles = "Manager")]
	public class ManagerFlowerVariantController : Controller
	{
		private readonly ApplicationDbContext _context;

		public ManagerFlowerVariantController(ApplicationDbContext context)
		{
			_context = context;
		}

	// Danh sách biến thể hoa
	public IActionResult Index(string? searchString, int? flowerTypeId)
	{
		var query = _context.FlowerVariants.AsQueryable();

		// Áp dụng bộ lọc tìm kiếm
		if (!string.IsNullOrEmpty(searchString))
		{
			query = query.Where(v => 
				v.Name.Contains(searchString) || 
				(v.Color != null && v.Color.Contains(searchString))
			);
		}

		// Áp dụng bộ lọc loại hoa
		if (flowerTypeId.HasValue)
		{
			query = query.Where(v => v.FlowerTypeId == flowerTypeId.Value);
		}

		var variants = query.ToList();

		// Tính toán thống kê
		var allVariants = _context.FlowerVariants.ToList();
		ViewBag.TotalVariants = allVariants.Count;
		ViewBag.LowStockVariants = allVariants.Count(v => v.Stock < 50 && v.Stock > 0);
		ViewBag.OutOfStockVariants = allVariants.Count(v => v.Stock == 0);

		// Truyền giá trị bộ lọc và danh sách loại hoa
		ViewBag.SearchString = searchString;
		ViewBag.FlowerTypeId = flowerTypeId;
		ViewBag.FlowerTypes = _context.FlowerTypes.ToList();

		return View(variants);
	}		// Hiển thị form thêm biến thể hoa
		public IActionResult Create()
		{
			ViewBag.FlowerTypes = _context.FlowerTypes.ToList();
			return View();
		}

		// Xử lý thêm biến thể hoa
		[HttpPost]
		public IActionResult Create(FlowerVariant model)
		{
			if (!ModelState.IsValid)
			{
				ViewBag.FlowerTypes = _context.FlowerTypes.ToList();
				return View(model);
			}
			_context.FlowerVariants.Add(model);
			_context.SaveChanges();
			return RedirectToAction("Index");
		}

		// Hiển thị form sửa biến thể hoa
		public IActionResult Edit(int id)
		{
			var variant = _context.FlowerVariants.Find(id);
			if (variant == null) return NotFound();
			ViewBag.FlowerTypes = _context.FlowerTypes.ToList();
			return View(variant);
		}

		// Xử lý sửa biến thể hoa
		[HttpPost]
		public IActionResult Edit(FlowerVariant model)
		{
			if (!ModelState.IsValid)
			{
				ViewBag.FlowerTypes = _context.FlowerTypes.ToList();
				return View(model);
			}
			_context.FlowerVariants.Update(model);
			_context.SaveChanges();
			return RedirectToAction("Index");
		}

		// Xóa biến thể hoa
		[HttpPost]
		public IActionResult Delete(int id)
		{
			var variant = _context.FlowerVariants.Find(id);
			if (variant == null) return NotFound();
			_context.FlowerVariants.Remove(variant);
			_context.SaveChanges();
			return RedirectToAction("Index");
		}

		// API: Lấy danh sách biến thể theo loại hoa (dùng cho AJAX)
		[HttpGet]
		public IActionResult GetVariantsByType(int flowerTypeId)
		{
			var variants = _context.FlowerVariants
				.Where(v => v.FlowerTypeId == flowerTypeId)
				.Select(v => new { v.Id, v.Name })
				.ToList();
			return Json(variants);
		}
	}
}
