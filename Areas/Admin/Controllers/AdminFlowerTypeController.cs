using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Linq;

namespace Bloomie.Areas.Admin.Controllers
{
	[Area("Admin")]
	[Authorize(Roles = "Admin")]
	public class AdminFlowerTypeController : Controller
	{
		private readonly ApplicationDbContext _context;

		public AdminFlowerTypeController(ApplicationDbContext context)
		{
			_context = context;
		}

		// Danh sách loại hoa
		public IActionResult Index(string? searchString)
		{
			var query = _context.FlowerTypes.AsQueryable();

			// Apply search filter
			if (!string.IsNullOrEmpty(searchString))
			{
				query = query.Where(f => 
					f.Name.Contains(searchString) || 
					(f.Description != null && f.Description.Contains(searchString))
				);
			}

			var types = query.ToList();

			// Calculate statistics
			var allFlowerTypes = _context.FlowerTypes.ToList();
			ViewBag.TotalFlowerTypes = allFlowerTypes.Count;
			ViewBag.FlowerTypesWithProducts = allFlowerTypes.Count; // Placeholder - adjust based on your data model

			// Pass search string to view
			ViewBag.SearchString = searchString;

			return View(types);
		}

		// Hiển thị form thêm loại hoa
		public IActionResult Create()
		{
			return View();
		}

		// Xử lý thêm loại hoa
		[HttpPost]
		public IActionResult Create(FlowerType model)
		{
			if (!ModelState.IsValid)
				return View(model);
			_context.FlowerTypes.Add(model);
			_context.SaveChanges();
			return RedirectToAction("Index");
		}

		// Hiển thị form sửa loại hoa
		public IActionResult Edit(int id)
		{
			var type = _context.FlowerTypes.Find(id);
			if (type == null) return NotFound();
			return View(type);
		}

		// Xử lý sửa loại hoa
		[HttpPost]
		public IActionResult Edit(FlowerType model)
		{
			if (!ModelState.IsValid)
				return View(model);
			_context.FlowerTypes.Update(model);
			_context.SaveChanges();
			return RedirectToAction("Index");
		}

		// Xóa loại hoa
		[HttpPost]
		public IActionResult Delete(int id)
		{
			var type = _context.FlowerTypes.Find(id);
			if (type == null) return NotFound();
			_context.FlowerTypes.Remove(type);
			_context.SaveChanges();
			return RedirectToAction("Index");
		}
	}
}
