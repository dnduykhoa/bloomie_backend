using Microsoft.AspNetCore.Mvc;
using Bloomie.Data;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models.Entities;

namespace Bloomie.ApiControllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HomeApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HomeApiController> _logger;

        public HomeApiController(ApplicationDbContext context, ILogger<HomeApiController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("data")]
        public async Task<IActionResult> GetHomeData()
        {
            try
            {
                var now = DateTime.Now;

                // Lấy tất cả discount đang active
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && 
                               d.StartDate <= now && 
                               (d.EndDate == null || d.EndDate >= now))
                    .ToListAsync();

                // Tính giá sau discount cho tất cả sản phẩm
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

                // Lấy sản phẩm mới nhất (8 sản phẩm)
                var newProducts = await _context.Products
                    .Include(p => p.Images)
                    .Where(p => p.IsActive)
                    .OrderByDescending(p => p.Id)
                    .Take(8)
                    .Select(p => new
                    {
                        id = p.Id,
                        name = p.Name,
                        price = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountedPrice 
                            : p.Price,
                        originalPrice = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].originalPrice 
                            : (decimal?)null,
                        discountType = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountType 
                            : null,
                        discountValue = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountValue 
                            : 0,
                        imageUrl = !string.IsNullOrEmpty(p.ImageUrl) 
                            ? p.ImageUrl 
                            : p.Images != null && p.Images.Any() 
                                ? p.Images.First().Url 
                                : "/images/placeholder.jpg",
                        url = $"/Product/Details/{p.Id}"
                    })
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
                    .Select(p => new
                    {
                        id = p.Id,
                        name = p.Name,
                        price = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountedPrice 
                            : p.Price,
                        originalPrice = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].originalPrice 
                            : (decimal?)null,
                        discountType = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountType 
                            : null,
                        discountValue = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountValue 
                            : 0,
                        imageUrl = !string.IsNullOrEmpty(p.ImageUrl) 
                            ? p.ImageUrl 
                            : p.Images != null && p.Images.Any() 
                                ? p.Images.First().Url 
                                : "/images/placeholder.jpg",
                        url = $"/Product/Details/{p.Id}"
                    })
                    .ToListAsync();

                // Lấy top 6 đánh giá tốt nhất
                var topReviews = await _context.Ratings
                    .Include(r => r.User)
                    .Include(r => r.Product)
                    .Where(r => r.Star >= 4 && r.IsVisible)
                    .OrderByDescending(r => r.Star)
                    .ThenByDescending(r => r.ReviewDate)
                    .Take(6)
                    .Select(r => new
                    {
                        id = r.Id,
                        userName = r.User != null ? r.User.FullName : "Khách hàng",
                        userAvatar = r.User != null ? r.User.ProfileImageUrl : null,
                        rating = r.Star,
                        comment = r.Comment,
                        reviewDate = r.ReviewDate,
                        productName = r.Product != null ? r.Product.Name : "",
                        productUrl = r.Product != null ? $"/Product/Details/{r.Product.Id}" : ""
                    })
                    .ToListAsync();

                // Lấy 3 blog posts mới nhất
                var latestBlogs = await _context.Blogs
                    .Where(b => b.IsPublished)
                    .OrderByDescending(b => b.PublishDate)
                    .Take(3)
                    .Select(b => new
                    {
                        id = b.Id,
                        title = b.Title,
                        excerpt = b.Excerpt,
                        imageUrl = b.ImageUrl,
                        publishDate = b.PublishDate,
                        url = $"/Blog/Details/{b.Id}"
                    })
                    .ToListAsync();

                return Ok(new
                {
                    newProducts,
                    promotionProducts,
                    topReviews,
                    latestBlogs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting home data");
                return StatusCode(500, new { error = "An error occurred while fetching home data", details = ex.Message });
            }
        }

        [HttpGet("new-products")]
        public async Task<IActionResult> GetNewProducts([FromQuery] int limit = 8)
        {
            try
            {
                var newProducts = await _context.Products
                    .Include(p => p.Images)
                    .Where(p => p.IsActive)
                    .OrderByDescending(p => p.Id)
                    .Take(limit)
                    .Select(p => new
                    {
                        id = p.Id,
                        name = p.Name,
                        price = p.Price,
                        imageUrl = !string.IsNullOrEmpty(p.ImageUrl) 
                            ? p.ImageUrl 
                            : p.Images != null && p.Images.Any() 
                                ? p.Images.First().Url 
                                : "/images/placeholder.jpg",
                        url = $"/Product/Details/{p.Id}"
                    })
                    .ToListAsync();

                return Ok(newProducts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting new products");
                return StatusCode(500, new { error = "An error occurred", details = ex.Message });
            }
        }

        [HttpGet("promotion-products")]
        public async Task<IActionResult> GetPromotionProducts([FromQuery] int limit = 8)
        {
            try
            {
                var now = DateTime.Now;

                // Lấy tất cả discount đang active
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && 
                               d.StartDate <= now && 
                               (d.EndDate == null || d.EndDate >= now))
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

                // Tính giá sau giảm
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
                            
                            if (!productDiscountPrices.ContainsKey(productId) || discountedPrice < productDiscountPrices[productId].discountedPrice)
                            {
                                productDiscountPrices[productId] = (product.Price, discountedPrice, discount.DiscountType, discount.DiscountValue);
                            }
                        }
                    }
                }

                var promotionProducts = await _context.Products
                    .Include(p => p.Images)
                    .Where(p => allPromotionProductIds.Contains(p.Id) && p.IsActive)
                    .Take(limit)
                    .Select(p => new
                    {
                        id = p.Id,
                        name = p.Name,
                        price = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountedPrice 
                            : p.Price,
                        originalPrice = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].originalPrice 
                            : (decimal?)null,
                        discountType = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountType 
                            : null,
                        discountValue = productDiscountPrices.ContainsKey(p.Id) 
                            ? productDiscountPrices[p.Id].discountValue 
                            : 0,
                        imageUrl = !string.IsNullOrEmpty(p.ImageUrl) 
                            ? p.ImageUrl 
                            : p.Images != null && p.Images.Any() 
                                ? p.Images.First().Url 
                                : "/images/placeholder.jpg",
                        url = $"/Product/Details/{p.Id}"
                    })
                    .ToListAsync();

                return Ok(promotionProducts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting promotion products");
                return StatusCode(500, new { error = "An error occurred", details = ex.Message });
            }
        }

        [HttpGet("reviews")]
        public async Task<IActionResult> GetTopReviews([FromQuery] int limit = 6)
        {
            try
            {
                var topReviews = await _context.Ratings
                    .Include(r => r.User)
                    .Include(r => r.Product)
                    .Where(r => r.Star >= 4 && r.IsVisible)
                    .OrderByDescending(r => r.Star)
                    .ThenByDescending(r => r.ReviewDate)
                    .Take(limit)
                    .Select(r => new
                    {
                        id = r.Id,
                        userName = r.User != null ? r.User.FullName : "Khách hàng",
                        userAvatar = r.User != null ? r.User.ProfileImageUrl : null,
                        rating = r.Star,
                        comment = r.Comment,
                        reviewDate = r.ReviewDate,
                        productName = r.Product != null ? r.Product.Name : "",
                        productUrl = r.Product != null ? $"/Product/Details/{r.Product.Id}" : ""
                    })
                    .ToListAsync();

                return Ok(topReviews);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting reviews");
                return StatusCode(500, new { error = "An error occurred", details = ex.Message });
            }
        }

        [HttpGet("blogs")]
        public async Task<IActionResult> GetLatestBlogs([FromQuery] int limit = 3)
        {
            try
            {
                var latestBlogs = await _context.Blogs
                    .Where(b => b.IsPublished)
                    .OrderByDescending(b => b.PublishDate)
                    .Take(limit)
                    .Select(b => new
                    {
                        id = b.Id,
                        title = b.Title,
                        excerpt = b.Excerpt,
                        imageUrl = b.ImageUrl,
                        publishDate = b.PublishDate,
                        url = $"/Blog/Details/{b.Id}"
                    })
                    .ToListAsync();

                return Ok(latestBlogs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting blogs");
                return StatusCode(500, new { error = "An error occurred", details = ex.Message });
            }
        }
    }
}
