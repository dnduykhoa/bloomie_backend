using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Linq;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminCategoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        public AdminCategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index(string searchString, int? type, int page = 1, int pageSize = 10)
        {
            // Base query
            var query = _context.Categories.AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(c => (c.Name != null && c.Name.Contains(searchString)) || 
                                         (c.Description != null && c.Description.Contains(searchString)));
            }

            if (type.HasValue)
            {
                query = query.Where(c => c.Type == type.Value);
            }

            // Calculate statistics
            var totalCategories = _context.Categories.Count();
            var topicCount = _context.Categories.Count(c => c.Type == 0);
            var recipientCount = _context.Categories.Count(c => c.Type == 1);
            var shapeCount = _context.Categories.Count(c => c.Type == 2);

            // Pass data to view
            ViewBag.SearchString = searchString;
            ViewBag.Type = type;
            ViewBag.TotalCategories = totalCategories;
            ViewBag.TopicCount = topicCount;
            ViewBag.RecipientCount = recipientCount;
            ViewBag.ShapeCount = shapeCount;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)query.Count() / pageSize);

            // Apply pagination
            var categories = query
                .OrderBy(c => c.Type)
                .ThenBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return View(categories);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Category model)
        {
            if (!ModelState.IsValid)
                return View(model);
            _context.Categories.Add(model);
            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        public IActionResult Edit(int id)
        {
            var category = _context.Categories.Find(id);
            if (category == null) return NotFound();
            return View(category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(Category model)
        {
            if (!ModelState.IsValid)
                return View(model);
            _context.Categories.Update(model);
            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        public IActionResult Delete(int id)
        {
            var category = _context.Categories.Find(id);
            if (category == null) return NotFound();
            _context.Categories.Remove(category);
            _context.SaveChanges();
            return RedirectToAction("Index");
        }
    }
}
