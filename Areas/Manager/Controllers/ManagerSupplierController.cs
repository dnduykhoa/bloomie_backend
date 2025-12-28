using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Linq;

namespace Bloomie.Areas.Manager.Controllers
{
    [Area("Manager")]
    [Authorize(Roles = "Manager")]
    public class ManagerSupplierController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ManagerSupplierController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Danh sách nhà cung cấp
        public IActionResult Index(string? searchString)
        {
            var query = _context.Suppliers.AsQueryable();

            // Apply search filter
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(s => 
                    s.Name.Contains(searchString) || 
                    s.Address.Contains(searchString) || 
                    s.Phone.Contains(searchString)
                );
            }

            var suppliers = query.ToList();

            // Calculate statistics
            var allSuppliers = _context.Suppliers.ToList();
            ViewBag.TotalSuppliers = allSuppliers.Count;
            ViewBag.ActiveSuppliers = allSuppliers.Count; // Assuming all are active
            ViewBag.SuppliersWithProducts = allSuppliers.Count; // Placeholder - adjust based on your data model

            // Pass search string to view
            ViewBag.SearchString = searchString;

            return View(suppliers);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Create(Supplier model)
        {
            if (!ModelState.IsValid)
                return View(model);
            _context.Suppliers.Add(model);
            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        public IActionResult Edit(int id)
        {
            var supplier = _context.Suppliers.Find(id);
            if (supplier == null) return NotFound();
            return View(supplier);
        }

        [HttpPost]
        public IActionResult Edit(Supplier model)
        {
            if (!ModelState.IsValid)
                return View(model);
            _context.Suppliers.Update(model);
            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult Delete(int id)
        {
            var supplier = _context.Suppliers.Find(id);
            if (supplier == null) return NotFound();
            _context.Suppliers.Remove(supplier);
            _context.SaveChanges();
            return RedirectToAction("Index");
        }
    }
}
