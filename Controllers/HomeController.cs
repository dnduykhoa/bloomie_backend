using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Bloomie.Models;
using Bloomie.Data;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models.Entities;

namespace Bloomie.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        if (User.Identity.IsAuthenticated)
        {
            // Kiểm tra role
            if (User.IsInRole("Admin"))
                return Redirect("/Admin/AdminDashboard");
            if (User.IsInRole("Staff"))
                return Redirect("/Staff/Home");
            if (User.IsInRole("Manager"))
                return Redirect("/Admin/AdminDashboard");
        }

        // Lấy tất cả discount đang active
        var now = DateTime.Now;
        var activeDiscounts = await _context.ProductDiscounts
            .Where(d => d.IsActive && 
                       d.StartDate <= now && 
                       (d.EndDate == null || d.EndDate >= now))
            .ToListAsync();

        // Lấy sản phẩm mới nhất (8 sản phẩm)
        var newProducts = await _context.Products
            .Include(p => p.Images)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.Id)
            .Take(8)
            .ToListAsync();

        // Lấy sản phẩm có promotion (từ PromotionProducts)
        var promotionProductIds = await _context.PromotionProducts
            .Include(pp => pp.Promotion)
            .Where(pp => pp.Promotion != null && 
                         pp.Promotion.IsActive && 
                         (pp.Promotion.EndDate == null || pp.Promotion.EndDate >= DateTime.Now))
            .Select(pp => pp.ProductId)
            .Distinct()
            .ToListAsync();

        // Lấy sản phẩm có discount (từ ProductDiscounts)
        var discountProductIds = new List<int>();
        foreach (var discount in activeDiscounts)
        {
            if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
            {
                var ids = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds) ?? new List<int>();
                discountProductIds.AddRange(ids);
            }
        }

        // Gộp cả 2 loại và lấy unique
        var allPromotionProductIds = promotionProductIds.Union(discountProductIds).Distinct().ToList();

        var promotionProducts = await _context.Products
            .Include(p => p.Images)
            .Where(p => allPromotionProductIds.Contains(p.Id) && p.IsActive)
            .Take(8)
            .ToListAsync();

        // Lấy top 6 đánh giá tốt nhất
        var topReviews = await _context.Ratings
            .Include(r => r.User)
            .Include(r => r.Product)
            .Where(r => r.Star >= 4 && r.IsVisible)
            .OrderByDescending(r => r.Star)
            .ThenByDescending(r => r.ReviewDate)
            .Take(6)
            .ToListAsync();

        // Tạo dictionary để lưu giá sau giảm cho từng sản phẩm
        var productDiscountPrices = new Dictionary<int, (decimal originalPrice, decimal discountedPrice, string discountType, decimal discountValue)>();
        
        foreach (var discount in activeDiscounts)
        {
            List<int> applicableProductIds = new List<int>();
            
            if (discount.ApplyTo == "all")
            {
                applicableProductIds = await _context.Products.Where(p => p.IsActive).Select(p => p.Id).ToListAsync();
            }
            else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
            {
                applicableProductIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds) ?? new List<int>();
            }
            else if (discount.ApplyTo == "categories" && !string.IsNullOrEmpty(discount.CategoryIds))
            {
                var categoryIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.CategoryIds) ?? new List<int>();
                applicableProductIds = await _context.ProductCategories
                    .Where(pc => categoryIds.Contains(pc.CategoryId))
                    .Select(pc => pc.ProductId)
                    .Distinct()
                    .ToListAsync();
            }
            
            foreach (var productId in applicableProductIds)
            {
                var product = await _context.Products.FindAsync(productId);
                if (product != null)
                {
                    decimal discountedPrice = product.Price;
                    
                    if (discount.DiscountType == "percent")
                    {
                        var discountAmount = product.Price * discount.DiscountValue / 100;
                        if (discount.MaxDiscount.HasValue && discountAmount > discount.MaxDiscount.Value)
                        {
                            discountAmount = discount.MaxDiscount.Value;
                        }
                        discountedPrice = product.Price - discountAmount;
                    }
                    else if (discount.DiscountType == "fixed_amount")
                    {
                        discountedPrice = product.Price - discount.DiscountValue;
                        if (discountedPrice < 0) discountedPrice = 0;
                    }
                    
                    // Chỉ lưu nếu giá mới thấp hơn (ưu tiên discount tốt nhất)
                    if (!productDiscountPrices.ContainsKey(productId) || discountedPrice < productDiscountPrices[productId].discountedPrice)
                    {
                        productDiscountPrices[productId] = (product.Price, discountedPrice, discount.DiscountType, discount.DiscountValue);
                    }
                }
            }
        }

        // Lấy 3 blog posts mới nhất
        var latestBlogs = await _context.Blogs
            .Where(b => b.IsPublished)
            .OrderByDescending(b => b.PublishDate)
            .Take(3)
            .ToListAsync();

        ViewBag.NewProducts = newProducts;
        ViewBag.PromotionProducts = promotionProducts;
        ViewBag.TopReviews = topReviews;
        ViewBag.ProductDiscountPrices = productDiscountPrices;
        ViewBag.LatestBlogs = latestBlogs;

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult TermsOfService()
    {
        return View();
    }

    public IActionResult PrivacyPolicy()
    {
        return View();
    }

    public IActionResult FlowerDetection()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
