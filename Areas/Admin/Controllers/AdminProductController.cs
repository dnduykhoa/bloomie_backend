using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminProductController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        
        public AdminProductController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public IActionResult Index(
            string searchString, 
            bool? isActive,
            string priceRange,
            int? categoryId,
            int? shapeId,
            string stockStatus,
            int page = 1, 
            int pageSize = 10)
        {
            var query = _context.Products
                .Include(p => p.ProductCategories).ThenInclude(pc => pc.Category)
                .Include(p => p.Images)
                .AsQueryable();

            // Filter by search string
            if (!string.IsNullOrEmpty(searchString))
                query = query.Where(p => p.Name.Contains(searchString));

            // Filter by active status
            if (isActive.HasValue)
                query = query.Where(p => p.IsActive == isActive.Value);

            // Filter by price range
            if (!string.IsNullOrEmpty(priceRange))
            {
                switch (priceRange)
                {
                    case "duoi250000":
                        query = query.Where(p => p.Price < 250000);
                        break;
                    case "250000-500000":
                        query = query.Where(p => p.Price >= 250000 && p.Price <= 500000);
                        break;
                    case "500000-1000000":
                        query = query.Where(p => p.Price >= 500000 && p.Price <= 1000000);
                        break;
                    case "1000000-2000000":
                        query = query.Where(p => p.Price >= 1000000 && p.Price <= 2000000);
                        break;
                    case "tren2000000":
                        query = query.Where(p => p.Price > 2000000);
                        break;
                }
            }

            // Filter by category
            if (categoryId.HasValue)
                query = query.Where(p => p.ProductCategories.Any(pc => pc.CategoryId == categoryId.Value));

            // Filter by shape
            if (shapeId.HasValue)
                query = query.Where(p => p.ProductCategories.Any(pc => pc.CategoryId == shapeId.Value && pc.Category.Type == (int)CategoryType.Shape));

            // Filter by stock status
            if (!string.IsNullOrEmpty(stockStatus))
            {
                switch (stockStatus)
                {
                    case "instock":
                        query = query.Where(p => p.StockQuantity > 10);
                        break;
                    case "lowstock":
                        query = query.Where(p => p.StockQuantity > 0 && p.StockQuantity <= 10);
                        break;
                    case "outofstock":
                        query = query.Where(p => p.StockQuantity == 0);
                        break;
                }
            }

            // Statistics
            var totalProducts = query.Count();
            var activeProducts = query.Count(p => p.IsActive);
            var inStockProducts = query.Count(p => p.StockQuantity > 0);
            var outOfStockProducts = query.Count(p => p.StockQuantity == 0);

            int totalPages = (int)Math.Ceiling((double)totalProducts / pageSize);

            var products = query
                .OrderByDescending(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Pass filter values to view
            ViewBag.SearchString = searchString;
            ViewBag.IsActive = isActive;
            ViewBag.PriceRange = priceRange;
            ViewBag.CategoryId = categoryId;
            ViewBag.ShapeId = shapeId;
            ViewBag.StockStatus = stockStatus;

            // Statistics
            ViewBag.TotalProducts = totalProducts;
            ViewBag.ActiveProducts = activeProducts;
            ViewBag.InStockProducts = inStockProducts;
            ViewBag.OutOfStockProducts = outOfStockProducts;

            // Categories for filter dropdown
            ViewBag.Categories = _context.Categories
                .Where(c => c.Type != (int)CategoryType.Shape)
                .OrderBy(c => c.Name)
                .ToList();
            ViewBag.ShapeCategories = _context.Categories
                .Where(c => c.Type == (int)CategoryType.Shape)
                .OrderBy(c => c.Name)
                .ToList();

            ViewData["CurrentPage"] = page;
            ViewData["TotalPages"] = totalPages;
            ViewData["TotalItems"] = totalProducts;

            return View(products);
        }

        public IActionResult Details(int id)
        {
            var product = _context.Products
                .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
                .Include(p => p.Images)
                .Include(p => p.ProductDetails)
                .ThenInclude(pd => pd.FlowerVariant)
                .ThenInclude(fv => fv.FlowerType)
                .FirstOrDefault(p => p.Id == id);
            
            if (product == null) return NotFound();
            
            return View(product);
        }

        public IActionResult Edit(int id)
        {
            var product = _context.Products
                .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
                .Include(p => p.Images)
                .Include(p => p.ProductDetails)
                .FirstOrDefault(p => p.Id == id);
            if (product == null) return NotFound();

            var vm = new Bloomie.Models.ViewModels.ProductViewModel
            {
                Name = product.Name,
                Price = product.Price,
                StockQuantity = product.StockQuantity,
                Description = product.Description,
                CategoryIds = product.ProductCategories?.Where(pc => pc.Category.Type != (int)CategoryType.Shape).Select(pc => pc.CategoryId).ToList() ?? new List<int>(),
                ShapeCategoryId = product.ProductCategories?.FirstOrDefault(pc => pc.Category.Type == (int)CategoryType.Shape)?.CategoryId,
                Flowers = product.ProductDetails?.Select(pd => new Bloomie.Models.ViewModels.ProductFlowerDetailVM {
                    FlowerVariantId = pd.FlowerVariantId,
                    FlowerTypeId = _context.FlowerVariants.Where(fv => fv.Id == pd.FlowerVariantId).Select(fv => fv.FlowerTypeId).FirstOrDefault(),
                    Quantity = pd.Quantity
                }).ToList() ?? new List<Bloomie.Models.ViewModels.ProductFlowerDetailVM>(),
                ExistingMainImageUrl = product.ImageUrl,
                ExistingSubImageUrls = product.Images?.Where(img => img.Url != product.ImageUrl).Select(img => img.Url).ToList() ?? new List<string>(),
                Id = product.Id
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(Bloomie.Models.ViewModels.ProductViewModel model)
        {
            // Nếu có ảnh cũ thì không bắt buộc upload lại
            if (!string.IsNullOrEmpty(model.ExistingMainImageUrl))
            {
                ModelState.Remove("MainImage");
            }
            // SubImages luôn là tùy chọn
            ModelState.Remove("SubImages");
            
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var product = _context.Products
                .Include(p => p.ProductCategories)
                .Include(p => p.ProductDetails)
                .Include(p => p.Images)
                .FirstOrDefault(p => p.Id == model.Id);
            if (product == null) return NotFound();

            product.Name = model.Name;
            product.Price = model.Price;
            product.StockQuantity = model.StockQuantity;
            product.Description = model.Description;

            // Update categories
            var oldCats = product.ProductCategories?.ToList() ?? new List<ProductCategory>();
            foreach (var pc in oldCats) _context.ProductCategories.Remove(pc);
            if (model.CategoryIds != null)
            {
                foreach (var catId in model.CategoryIds)
                {
                    _context.ProductCategories.Add(new ProductCategory
                    {
                        ProductId = product.Id,
                        CategoryId = catId
                    });
                }
            }
            if (model.ShapeCategoryId.HasValue)
            {
                _context.ProductCategories.Add(new ProductCategory
                {
                    ProductId = product.Id,
                    CategoryId = model.ShapeCategoryId.Value
                });
            }

            // Update flower details
            var oldDetails = product.ProductDetails?.ToList() ?? new List<ProductDetail>();
            foreach (var pd in oldDetails) _context.ProductDetails.Remove(pd);
            if (model.Flowers != null)
            {
                foreach (var flower in model.Flowers)
                {
                    _context.ProductDetails.Add(new ProductDetail
                    {
                        ProductId = product.Id,
                        FlowerVariantId = flower.FlowerVariantId,
                        Quantity = flower.Quantity
                    });
                }
            }

            // Update main image: giữ lại ảnh cũ nếu không upload mới
            if (model.MainImage != null && model.MainImage.Length > 0)
            {
                var fileName = Path.GetFileNameWithoutExtension(model.MainImage.FileName) + "_" + Guid.NewGuid().ToString().Substring(0, 8) + Path.GetExtension(model.MainImage.FileName);
                var uploadPath = Path.Combine(_env.WebRootPath, "images/products", fileName);
                var dir = Path.GetDirectoryName(uploadPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (var stream = new FileStream(uploadPath, FileMode.Create))
                {
                    model.MainImage.CopyTo(stream);
                }
                product.ImageUrl = "/images/products/" + fileName;
                _context.ProductImages.Add(new ProductImage
                {
                    ProductId = product.Id,
                    Url = product.ImageUrl
                });
            }
            // Nếu không upload mới thì giữ lại ảnh cũ
            // Không xóa ProductImages cũ

            // Update sub images: chỉ thêm mới, không xóa ảnh cũ
            if (model.SubImages != null && model.SubImages.Count > 0)
            {
                foreach (var file in model.SubImages)
                {
                    if (file.Length > 0)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file.FileName) + "_" + Guid.NewGuid().ToString().Substring(0, 8) + Path.GetExtension(file.FileName);
                        var uploadPath = Path.Combine(_env.WebRootPath, "images/products", fileName);
                        var dir = Path.GetDirectoryName(uploadPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        using (var stream = new FileStream(uploadPath, FileMode.Create))
                        {
                            file.CopyTo(stream);
                        }
                        _context.ProductImages.Add(new ProductImage
                        {
                            ProductId = product.Id,
                            Url = "/images/products/" + fileName
                        });
                    }
                }
            }

            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Bloomie.Models.ViewModels.ProductViewModel model)
        {
            // SubImages là tùy chọn
            ModelState.Remove("SubImages");
            
            if (!ModelState.IsValid)
                return View(model);

            // Tạo Product entity
            var product = new Product
            {
                Name = model.Name,
                Description = model.Description,
                Price = model.Price,
                StockQuantity = model.StockQuantity,
                IsActive = true
            };
            _context.Products.Add(product);
            _context.SaveChanges();

            // Gán danh mục (nhiều)
            if (model.CategoryIds != null)
            {
                foreach (var catId in model.CategoryIds)
                {
                    _context.ProductCategories.Add(new ProductCategory
                    {
                        ProductId = product.Id,
                        CategoryId = catId
                    });
                }
            }

            // Gán kiểu trình bày (ShapeCategoryId)
            if (model.ShapeCategoryId.HasValue)
            {
                _context.ProductCategories.Add(new ProductCategory
                {
                    ProductId = product.Id,
                    CategoryId = model.ShapeCategoryId.Value
                });
            }

            // Gán chi tiết hoa (Flowers)
            if (model.Flowers != null)
            {
                foreach (var flower in model.Flowers)
                {
                    _context.ProductDetails.Add(new ProductDetail
                    {
                        ProductId = product.Id,
                        FlowerVariantId = flower.FlowerVariantId,
                        Quantity = flower.Quantity
                    });
                }
            }

            // Lưu ảnh chính
            if (model.MainImage != null && model.MainImage.Length > 0)
            {
                var fileName = Path.GetFileNameWithoutExtension(model.MainImage.FileName) + "_" + Guid.NewGuid().ToString().Substring(0, 8) + Path.GetExtension(model.MainImage.FileName);
                var uploadPath = Path.Combine(_env.WebRootPath, "images/products", fileName);
                var dir = Path.GetDirectoryName(uploadPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (var stream = new FileStream(uploadPath, FileMode.Create))
                {
                    model.MainImage.CopyTo(stream);
                }
                _context.ProductImages.Add(new ProductImage
                {
                    ProductId = product.Id,
                    Url = "/images/products/" + fileName
                });
                // Optionally set product.ImageUrl = ...
                product.ImageUrl = "/images/products/" + fileName;
            }

            // Lưu ảnh phụ
            if (model.SubImages != null && model.SubImages.Count > 0)
            {
                foreach (var file in model.SubImages)
                {
                    if (file.Length > 0)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file.FileName) + "_" + Guid.NewGuid().ToString().Substring(0, 8) + Path.GetExtension(file.FileName);
                        var uploadPath = Path.Combine(_env.WebRootPath, "images/products", fileName);
                        var dir = Path.GetDirectoryName(uploadPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        using (var stream = new FileStream(uploadPath, FileMode.Create))
                        {
                            file.CopyTo(stream);
                        }
                        _context.ProductImages.Add(new ProductImage
                        {
                            ProductId = product.Id,
                            Url = "/images/products/" + fileName
                        });
                    }
                }
            }

            _context.SaveChanges();
            return RedirectToAction("Index");
        }
    }
}
