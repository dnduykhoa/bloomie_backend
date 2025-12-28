using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Security.Claims;

namespace Bloomie.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProductApiController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: api/ProductApi/products
        /// <summary>
        /// Lấy danh sách sản phẩm với filter
        /// </summary>
        [HttpGet("products")]
        public async Task<IActionResult> GetProducts(
            [FromQuery] int? categoryId,
            [FromQuery] string? searchString,
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] string? colors, // Comma-separated colors: "Đỏ,Vàng,Hồng"
            [FromQuery] string? flowerTypeIds, // Comma-separated IDs: "1,2,3"
            [FromQuery] string? shapeIds, // Comma-separated IDs: "5,6,7"
            [FromQuery] double? minRating,
            [FromQuery] int? minSold,
            [FromQuery] string? sortBy,
            [FromQuery] bool? onlyDiscount,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12)
        {
            try
            {
                var productsQuery = _context.Products
                    .Include(p => p.Images!)
                    .Include(p => p.ProductCategories!)
                        .ThenInclude(pc => pc.Category!)
                    .Include(p => p.ProductDetails!)
                        .ThenInclude(pd => pd.FlowerVariant!)
                    .Where(p => p.IsActive);

                // Áp dụng filters cơ bản
                if (categoryId.HasValue)
                {
                    productsQuery = productsQuery.Where(p => 
                        p.ProductCategories != null && 
                        p.ProductCategories.Any(pc => pc.CategoryId == categoryId.Value));
                }

                if (!string.IsNullOrEmpty(searchString))
                {
                    productsQuery = productsQuery.Where(p => 
                        p.Name != null && p.Name.Contains(searchString));
                }

                if (minPrice.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Price >= minPrice.Value);
                }

                if (maxPrice.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Price <= maxPrice.Value);
                }

                // Lọc theo màu sắc (từ FlowerVariant trong ProductDetails)
                if (!string.IsNullOrEmpty(colors))
                {
                    var colorList = colors.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(c => c.Trim().ToLower())
                        .ToList();
                    if (colorList.Any())
                    {
                        productsQuery = productsQuery.Where(p => 
                            p.ProductDetails != null && 
                            p.ProductDetails.Any(pd => 
                                pd.FlowerVariant != null && 
                                pd.FlowerVariant.Color != null &&
                                colorList.Contains(pd.FlowerVariant.Color.Trim().ToLower())));
                    }
                }

                // Lọc theo loại hoa (từ FlowerVariant trong ProductDetails)
                if (!string.IsNullOrEmpty(flowerTypeIds))
                {
                    var flowerTypeIdList = flowerTypeIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => int.TryParse(id.Trim(), out var result) ? result : (int?)null)
                        .Where(id => id.HasValue)
                        .Select(id => id!.Value)
                        .ToList();
                    
                    if (flowerTypeIdList.Any())
                    {
                        productsQuery = productsQuery.Where(p => 
                            p.ProductDetails != null && 
                            p.ProductDetails.Any(pd => 
                                pd.FlowerVariant != null && 
                                flowerTypeIdList.Contains(pd.FlowerVariant.FlowerTypeId)));
                    }
                }

                // Lọc theo cách trình bày (shape/presentation)
                if (!string.IsNullOrEmpty(shapeIds))
                {
                    var shapeIdList = shapeIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => int.TryParse(id.Trim(), out var result) ? result : (int?)null)
                        .Where(id => id.HasValue)
                        .Select(id => id!.Value)
                        .ToList();
                    
                    if (shapeIdList.Any())
                    {
                        productsQuery = productsQuery.Where(p => 
                            p.ProductCategories != null && 
                            p.ProductCategories.Any(pc => 
                                shapeIdList.Contains(pc.CategoryId) && 
                                pc.Category != null && 
                                pc.Category.Type == 2)); // Chỉ lấy category Type = Shape (2)
                    }
                }

                // Lấy tất cả sản phẩm để áp dụng filter phức tạp (rating, sold, discount)
                var allProducts = await productsQuery.ToListAsync();

                // Lấy productIds để tính rating và sold
                var productIds = allProducts.Select(p => p.Id).ToList();

                // Tính rating cho các sản phẩm
                var ratingsData = await _context.Ratings
                    .Where(r => productIds.Contains(r.ProductId) && r.IsVisible)
                    .GroupBy(r => r.ProductId)
                    .Select(g => new { ProductId = g.Key, AvgRating = g.Average(r => (double)r.Star), Count = g.Count() })
                    .ToListAsync();

                var ratingsDict = ratingsData.ToDictionary(r => r.ProductId, r => (r.AvgRating, r.Count));

                // Tính số lượng đã bán cho các sản phẩm
                var soldData = await _context.OrderDetails
                    .Include(od => od.Order)
                    .Where(od => productIds.Contains(od.ProductId) && od.Order != null && od.Order.Status == "Hoàn thành")
                    .GroupBy(od => od.ProductId)
                    .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(od => od.Quantity) })
                    .ToListAsync();

                var soldDict = soldData.ToDictionary(s => s.ProductId, s => s.TotalSold);

                // Lấy ProductDiscount đang active
                var now = DateTime.Now;
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .ToListAsync();

                // ✅ Lấy danh sách sản phẩm trong wishlist của user (nếu đã đăng nhập)
                var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
                var wishlistProductIds = new HashSet<int>();
                if (!string.IsNullOrEmpty(userId))
                {
                    wishlistProductIds = await _context.WishLists
                        .Where(w => w.UserId == userId && productIds.Contains(w.ProductId))
                        .Select(w => w.ProductId)
                        .ToHashSetAsync();
                }

                // Tính giảm giá cho từng sản phẩm
                var productsWithDiscount = allProducts.Select(p => {
                    decimal discountAmount = 0;
                    string? discountType = null;
                    decimal? discountValue = null;

                    foreach (var discount in activeDiscounts)
                    {
                        bool isApplicable = false;

                        if (discount.ApplyTo == "all")
                        {
                            isApplicable = true;
                        }
                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                        {
                            var productIdsList = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                            if (productIdsList != null && productIdsList.Contains(p.Id))
                            {
                                isApplicable = true;
                            }
                        }
                        else if (discount.ApplyTo == "categories" && !string.IsNullOrEmpty(discount.CategoryIds))
                        {
                            var categoryIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.CategoryIds);
                            var productCategoryIds = p.ProductCategories?.Select(pc => pc.CategoryId).ToList() ?? new List<int>();
                            if (categoryIds != null && categoryIds.Any(cid => productCategoryIds.Contains(cid)))
                            {
                                isApplicable = true;
                            }
                        }

                        if (isApplicable)
                        {
                            decimal tempDiscount = 0;
                            if (discount.DiscountType == "percent")
                            {
                                tempDiscount = p.Price * (discount.DiscountValue / 100);
                                if (discount.MaxDiscount.HasValue && tempDiscount > discount.MaxDiscount.Value)
                                {
                                    tempDiscount = discount.MaxDiscount.Value;
                                }
                            }
                            else if (discount.DiscountType == "fixed_amount")
                            {
                                tempDiscount = discount.DiscountValue;
                            }

                            if (tempDiscount > discountAmount)
                            {
                                discountAmount = tempDiscount;
                                discountType = discount.DiscountType;
                                discountValue = discount.DiscountValue;
                            }
                        }
                    }

                    // Lấy thông tin rating và sold
                    var hasRating = ratingsDict.TryGetValue(p.Id, out var ratingInfo);
                    var avgRating = hasRating ? ratingInfo.AvgRating : 0;
                    var totalReviews = hasRating ? ratingInfo.Count : 0;
                    var totalSold = soldDict.TryGetValue(p.Id, out var sold) ? sold : 0;

                    // Sắp xếp ảnh: ảnh chính lên đầu
                    var imagesList = p.Images!.Select(i => new { i.Id, i.Url }).ToList();
                    var primaryImageUrl = p.ImageUrl;
                    if (!string.IsNullOrEmpty(primaryImageUrl))
                    {
                        // Loại bỏ ảnh chính khỏi danh sách nếu đã tồn tại
                        imagesList = imagesList.Where(i => i.Url != primaryImageUrl).ToList();
                        // Thêm ảnh chính vào đầu danh sách
                        imagesList.Insert(0, new { Id = 0, Url = primaryImageUrl });
                    }

                    return new
                    {
                        p.Id,
                        p.Name,
                        p.Description,
                        p.Price,
                        p.ImageUrl,
                        p.StockQuantity,
                        p.IsActive,
                        Images = imagesList,
                        Categories = p.ProductCategories!.Select(pc => new
                        {
                            pc.CategoryId,
                            pc.Category!.Name,
                            pc.Category.ParentId
                        }).ToList(),
                        // Thông tin giảm giá
                        DiscountAmount = discountAmount,
                        DiscountType = discountType,
                        DiscountValue = discountValue,
                        DiscountedPrice = p.Price - discountAmount,
                        HasDiscount = discountAmount > 0,
                        // Thông tin rating và đã bán
                        AverageRating = Math.Round(avgRating, 1),
                        TotalReviews = totalReviews,
                        TotalSold = totalSold,
                        // ✅ Wishlist status
                        IsInWishlist = wishlistProductIds.Contains(p.Id)
                    };
                }).ToList();

                // Áp dụng filter theo rating tối thiểu
                if (minRating.HasValue)
                {
                    productsWithDiscount = productsWithDiscount.Where(p => p.AverageRating >= minRating.Value).ToList();
                }

                // Áp dụng filter theo số lượng đã bán tối thiểu
                if (minSold.HasValue)
                {
                    productsWithDiscount = productsWithDiscount.Where(p => p.TotalSold >= minSold.Value).ToList();
                }

                // Áp dụng filter chỉ sản phẩm giảm giá
                if (onlyDiscount.HasValue && onlyDiscount.Value)
                {
                    productsWithDiscount = productsWithDiscount.Where(p => p.HasDiscount).ToList();
                }

                // Sắp xếp theo tiêu chí
                productsWithDiscount = sortBy?.ToLower() switch
                {
                    "price_asc" => productsWithDiscount.OrderBy(p => p.DiscountedPrice).ToList(),
                    "price_desc" => productsWithDiscount.OrderByDescending(p => p.DiscountedPrice).ToList(),
                    "name_asc" => productsWithDiscount.OrderBy(p => p.Name).ToList(),
                    "name_desc" => productsWithDiscount.OrderByDescending(p => p.Name).ToList(),
                    "rating" => productsWithDiscount.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.TotalReviews).ToList(),
                    "sold" => productsWithDiscount.OrderByDescending(p => p.TotalSold).ToList(),
                    "discount" => productsWithDiscount.OrderByDescending(p => p.DiscountAmount).ToList(),
                    "newest" => productsWithDiscount.OrderByDescending(p => p.Id).ToList(),
                    _ => productsWithDiscount
                };

                // Tổng số sản phẩm sau khi filter
                var totalProducts = productsWithDiscount.Count;

                // Phân trang
                var pagedProducts = productsWithDiscount
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new
                {
                    success = true,
                    data = pagedProducts,
                    pagination = new
                    {
                        currentPage = page,
                        pageSize = pageSize,
                        totalItems = totalProducts,
                        totalPages = (int)Math.Ceiling(totalProducts / (double)pageSize)
                    },
                    filters = new
                    {
                        categoryId,
                        searchString,
                        minPrice,
                        maxPrice,
                        colors,
                        flowerTypeIds,
                        shapeIds,
                        minRating,
                        minSold,
                        onlyDiscount,
                        sortBy
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách sản phẩm", error = ex.Message });
            }
        }

        // GET: api/ProductApi/products/{id}
        /// <summary>
        /// Lấy chi tiết sản phẩm theo ID
        /// </summary>
        [HttpGet("products/{id}")]
        public async Task<IActionResult> GetProductDetails(int id)
        {
            try
            {
                var product = await _context.Products
                    .Include(p => p.Images!)
                    .Include(p => p.ProductDetails!)
                        .ThenInclude(pd => pd.FlowerVariant!)
                    .Include(p => p.ProductCategories!)
                        .ThenInclude(pc => pc.Category!)
                    .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);

                if (product == null)
                {
                    return NotFound(new { success = false, message = "Sản phẩm không tồn tại hoặc đã bị xóa" });
                }

                // Lấy đánh giá
                var ratings = await _context.Ratings
                    .Include(r => r.User)
                    .Include(r => r.Images)
                    .Include(r => r.Replies)
                        .ThenInclude(reply => reply.User)
                    .Include(r => r.Replies)
                        .ThenInclude(reply => reply.Images)
                    .Include(r => r.UserLikes)
                    .Where(r => r.ProductId == id && r.IsVisible)
                    .OrderByDescending(r => r.IsPinned) // Đánh giá được ghim lên đầu
                    .ThenByDescending(r => r.ReviewDate) // Sau đó sắp xếp theo thời gian
                    .AsSplitQuery()
                    .Select(r => new
                    {
                        r.Id,
                        r.Star,
                        r.Comment,
                        r.ReviewDate,
                        r.LikesCount,
                        r.IsVisible,
                        r.IsPinned, // Thêm trường ghim
                        User = new
                        {
                            r.User.Id,
                            r.User.UserName,
                            r.User.FullName,
                            r.User.ProfileImageUrl
                        },
                        Images = r.Images.Select(i => new { i.Id, i.ImageUrl, i.UploadedAt }).ToList(),
                        Replies = r.Replies.Where(rep => rep.IsVisible).Select(rep => new
                        {
                            rep.Id,
                            rep.Comment,
                            rep.ReplyDate,
                            rep.LikesCount,
                            rep.IsVisible,
                            User = _context.UserRoles.Any(ur => ur.UserId == rep.User.Id && 
                                _context.Roles.Any(role => role.Id == ur.RoleId && (role.Name == "Admin" || role.Name == "Manager" || role.Name == "Staff")))
                                ? new
                                {
                                    rep.User.Id,
                                    UserName = (string?)"bloomie_shop",
                                    FullName = "Bloomie Shop",
                                    ProfileImageUrl = (string?)"/images/logos/bloomie_logo.png"
                                }
                                : new
                                {
                                    rep.User.Id,
                                    rep.User.UserName,
                                    rep.User.FullName,
                                    rep.User.ProfileImageUrl
                                },
                            Images = rep.Images.Select(i => new { i.Id, i.ImageUrl, i.UploadedAt }).ToList()
                        }).ToList(),
                        IsLiked = User.Identity!.IsAuthenticated && r.UserLikes.Any(ul => ul.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                    })
                    .ToListAsync();

                var averageRating = ratings.Any() ? ratings.Average(r => r.Star) : 0;

                // Tính giảm giá cho sản phẩm chính
                var now = DateTime.Now;
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .ToListAsync();

                decimal productDiscountAmount = 0;
                string? productDiscountType = null;
                decimal? productDiscountValue = null;

                foreach (var discount in activeDiscounts)
                {
                    bool isApplicable = false;

                    if (discount.ApplyTo == "all")
                    {
                        isApplicable = true;
                    }
                    else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                    {
                        var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                        if (productIds != null && productIds.Contains(product.Id))
                        {
                            isApplicable = true;
                        }
                    }
                    else if (discount.ApplyTo == "categories" && !string.IsNullOrEmpty(discount.CategoryIds))
                    {
                        var categoryIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.CategoryIds);
                        var productCategoryIds = product.ProductCategories?.Select(pc => pc.CategoryId).ToList() ?? new List<int>();
                        if (categoryIds != null && categoryIds.Any(cid => productCategoryIds.Contains(cid)))
                        {
                            isApplicable = true;
                        }
                    }

                    if (isApplicable)
                    {
                        decimal tempDiscount = 0;
                        if (discount.DiscountType == "percent")
                        {
                            tempDiscount = product.Price * (discount.DiscountValue / 100);
                            if (discount.MaxDiscount.HasValue && tempDiscount > discount.MaxDiscount.Value)
                            {
                                tempDiscount = discount.MaxDiscount.Value;
                            }
                        }
                        else if (discount.DiscountType == "fixed_amount")
                        {
                            tempDiscount = discount.DiscountValue;
                        }

                        if (tempDiscount > productDiscountAmount)
                        {
                            productDiscountAmount = tempDiscount;
                            productDiscountType = discount.DiscountType;
                            productDiscountValue = discount.DiscountValue;
                        }
                    }
                }

                // Tính số lượng đã bán cho sản phẩm chính
                var productTotalSold = await _context.OrderDetails
                    .Include(od => od.Order)
                    .Where(od => od.ProductId == id && od.Order != null && od.Order.Status == "Hoàn thành")
                    .SumAsync(od => (int?)od.Quantity) ?? 0;

                // Sản phẩm tương tự
                var similarProductsList = new List<Product>();
                if (product.ProductCategories != null && product.ProductCategories.Any())
                {
                    var categoryIds = product.ProductCategories.Select(x => x.CategoryId).ToList();
                    similarProductsList = await _context.Products
                        .Include(p => p.Images!)
                        .Include(p => p.ProductCategories!)
                        .Where(p => p.Id != id && p.IsActive && 
                                   p.ProductCategories != null && 
                                   p.ProductCategories.Any(pc => categoryIds.Contains(pc.CategoryId)))
                        .Take(8)
                        .ToListAsync();
                }

                // Tính rating và sold cho sản phẩm tương tự
                var similarProductIds = similarProductsList.Select(p => p.Id).ToList();
                
                // ✅ Lấy userId trước để dùng cho wishlist check
                var currentUserId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
                
                var similarRatingsData = await _context.Ratings
                    .Where(r => similarProductIds.Contains(r.ProductId) && r.IsVisible)
                    .GroupBy(r => r.ProductId)
                    .Select(g => new { ProductId = g.Key, AvgRating = g.Average(r => (double)r.Star), Count = g.Count() })
                    .ToListAsync();
                
                var similarRatingsDict = similarRatingsData.ToDictionary(r => r.ProductId, r => (r.AvgRating, r.Count));
                
                var similarSoldData = await _context.OrderDetails
                    .Include(od => od.Order)
                    .Where(od => similarProductIds.Contains(od.ProductId) && od.Order != null && od.Order.Status == "Hoàn thành")
                    .GroupBy(od => od.ProductId)
                    .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(od => od.Quantity) })
                    .ToListAsync();
                
                var similarSoldDict = similarSoldData.ToDictionary(s => s.ProductId, s => s.TotalSold);

                // ✅ Kiểm tra sản phẩm tương tự có trong wishlist không
                var similarWishlistProductIds = new HashSet<int>();
                if (!string.IsNullOrEmpty(currentUserId))
                {
                    similarWishlistProductIds = await _context.WishLists
                        .Where(w => w.UserId == currentUserId && similarProductIds.Contains(w.ProductId))
                        .Select(w => w.ProductId)
                        .ToHashSetAsync();
                }

                // Tính giảm giá cho sản phẩm tương tự
                var similarProducts = similarProductsList.Select(p => {
                    decimal discountAmount = 0;
                    string? discountType = null;
                    decimal? discountValue = null;

                    foreach (var discount in activeDiscounts)
                    {
                        bool isApplicable = false;

                        if (discount.ApplyTo == "all")
                        {
                            isApplicable = true;
                        }
                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                        {
                            var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                            if (productIds != null && productIds.Contains(p.Id))
                            {
                                isApplicable = true;
                            }
                        }
                        else if (discount.ApplyTo == "categories" && !string.IsNullOrEmpty(discount.CategoryIds))
                        {
                            var categoryIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.CategoryIds);
                            var productCategoryIds = p.ProductCategories?.Select(pc => pc.CategoryId).ToList() ?? new List<int>();
                            if (categoryIds != null && categoryIds.Any(cid => productCategoryIds.Contains(cid)))
                            {
                                isApplicable = true;
                            }
                        }

                        if (isApplicable)
                        {
                            decimal tempDiscount = 0;
                            if (discount.DiscountType == "percent")
                            {
                                tempDiscount = p.Price * (discount.DiscountValue / 100);
                                if (discount.MaxDiscount.HasValue && tempDiscount > discount.MaxDiscount.Value)
                                {
                                    tempDiscount = discount.MaxDiscount.Value;
                                }
                            }
                            else if (discount.DiscountType == "fixed_amount")
                            {
                                tempDiscount = discount.DiscountValue;
                            }

                            if (tempDiscount > discountAmount)
                            {
                                discountAmount = tempDiscount;
                                discountType = discount.DiscountType;
                                discountValue = discount.DiscountValue;
                            }
                        }
                    }

                    // Lấy thông tin rating và sold cho sản phẩm tương tự
                    var hasRating = similarRatingsDict.TryGetValue(p.Id, out var ratingInfo);
                    var avgRating = hasRating ? ratingInfo.AvgRating : 0;
                    var totalReviews = hasRating ? ratingInfo.Count : 0;
                    var totalSold = similarSoldDict.TryGetValue(p.Id, out var sold) ? sold : 0;

                    // Sắp xếp ảnh: ảnh chính lên đầu
                    var imagesList = p.Images!.Select(i => new { i.Id, i.Url }).ToList();
                    var primaryImageUrl = p.ImageUrl;
                    if (!string.IsNullOrEmpty(primaryImageUrl))
                    {
                        // Loại bỏ ảnh chính khỏi danh sách nếu đã tồn tại
                        imagesList = imagesList.Where(i => i.Url != primaryImageUrl).ToList();
                        // Thêm ảnh chính vào đầu danh sách
                        imagesList.Insert(0, new { Id = 0, Url = primaryImageUrl });
                    }

                    return new
                    {
                        p.Id,
                        p.Name,
                        p.Price,
                        p.ImageUrl,
                        Images = imagesList,
                        DiscountAmount = discountAmount,
                        DiscountType = discountType,
                        DiscountValue = discountValue,
                        DiscountedPrice = p.Price - discountAmount,
                        HasDiscount = discountAmount > 0,
                        // Thông tin rating và đã bán
                        AverageRating = Math.Round(avgRating, 1),
                        TotalReviews = totalReviews,
                        TotalSold = totalSold,
                        // ✅ Wishlist status
                        IsInWishlist = similarWishlistProductIds.Contains(p.Id)
                    };
                }).ToList();

                // Sắp xếp ảnh cho sản phẩm chính: ảnh chính lên đầu
                var productImagesList = product.Images!.Select(i => new { i.Id, i.Url }).ToList();
                var productPrimaryImageUrl = product.ImageUrl;
                if (!string.IsNullOrEmpty(productPrimaryImageUrl))
                {
                    // Loại bỏ ảnh chính khỏi danh sách nếu đã tồn tại
                    productImagesList = productImagesList.Where(i => i.Url != productPrimaryImageUrl).ToList();
                    // Thêm ảnh chính vào đầu danh sách
                    productImagesList.Insert(0, new { Id = 0, Url = productPrimaryImageUrl });
                }

                // ✅ Kiểm tra sản phẩm có trong wishlist không (đã khai báo currentUserId ở trên)
                var isInWishlist = false;
                if (!string.IsNullOrEmpty(currentUserId))
                {
                    isInWishlist = await _context.WishLists
                        .AnyAsync(w => w.UserId == currentUserId && w.ProductId == id);
                }

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        product = new
                        {
                            product.Id,
                            product.Name,
                            product.Description,
                            product.Price,
                            product.ImageUrl,
                            product.StockQuantity,
                            product.IsActive,
                            Images = productImagesList,
                            ProductDetails = product.ProductDetails!.Select(pd => new
                            {
                                pd.Id,
                                pd.FlowerVariantId,
                                pd.Quantity,
                                FlowerVariant = pd.FlowerVariant != null ? new
                                {
                                    pd.FlowerVariant.Id,
                                    pd.FlowerVariant.Name,
                                    pd.FlowerVariant.Color,
                                    pd.FlowerVariant.FlowerTypeId
                                } : null
                            }).ToList(),
                            Categories = product.ProductCategories!.Select(pc => new
                            {
                                pc.CategoryId,
                                pc.Category!.Name,
                                pc.Category.ParentId
                            }).ToList(),
                            // Thông tin giảm giá
                            DiscountAmount = productDiscountAmount,
                            DiscountType = productDiscountType,
                            DiscountValue = productDiscountValue,
                            DiscountedPrice = product.Price - productDiscountAmount,
                            HasDiscount = productDiscountAmount > 0,
                            // Thông tin đã bán
                            TotalSold = productTotalSold,
                            // ✅ Wishlist status
                            IsInWishlist = isInWishlist
                        },
                        ratings = ratings,
                        averageRating = averageRating,
                        totalRatings = ratings.Count,
                        similarProducts = similarProducts
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy chi tiết sản phẩm", error = ex.Message });
            }
        }

        // GET: api/ProductApi/categories
        /// <summary>
        /// Lấy danh sách danh mục
        /// </summary>
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                var categories = await _context.Categories
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.ParentId,
                        c.Description
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = categories });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách danh mục", error = ex.Message });
            }
        }

        // POST: api/ProductApi/ratings
        /// <summary>
        /// Gửi đánh giá sản phẩm
        /// </summary>
        [HttpPost("ratings")]
        public async Task<IActionResult> SubmitRating([FromForm] int productId, [FromForm] int star, [FromForm] string comment, [FromForm] IFormFileCollection? ratingImages)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để đánh giá" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                // Validate
                if (star < 1 || star > 5)
                {
                    return BadRequest(new { success = false, message = "Số sao phải từ 1 đến 5" });
                }

                if (string.IsNullOrWhiteSpace(comment))
                {
                    return BadRequest(new { success = false, message = "Nội dung đánh giá không được để trống" });
                }

                var product = await _context.Products.FindAsync(productId);
                if (product == null || !product.IsActive)
                {
                    return NotFound(new { success = false, message = "Sản phẩm không tồn tại" });
                }

                var rating = new Rating
                {
                    ProductId = productId,
                    Star = star,
                    Comment = comment,
                    ReviewDate = DateTime.Now,
                    UserId = userId!,
                    IsVisible = true,
                    ImageUrl = "",
                    LastModifiedBy = userId!
                };

                _context.Ratings.Add(rating);
                await _context.SaveChangesAsync();

                // Xử lý upload ảnh
                if (ratingImages != null && ratingImages.Count > 0)
                {
                    var uploads = Path.Combine(_webHostEnvironment.WebRootPath, "images", "ratings");
                    if (!Directory.Exists(uploads))
                    {
                        Directory.CreateDirectory(uploads);
                    }

                    foreach (var file in ratingImages)
                    {
                        if (file != null && file.Length > 0)
                        {
                            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                            
                            if (!allowedExtensions.Contains(ext))
                            {
                                continue;
                            }

                            var fileName = Guid.NewGuid().ToString() + ext;
                            var filePath = Path.Combine(uploads, fileName);
                            
                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }

                            var imageUrl = "/images/ratings/" + fileName;
                            var ratingImageEntity = new RatingImage
                            {
                                RatingId = rating.Id,
                                ImageUrl = imageUrl,
                                UploadedAt = DateTime.Now
                            };
                            _context.Add(ratingImageEntity);
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                // Lấy lại rating với thông tin đầy đủ
                var savedRating = await _context.Ratings
                    .Include(r => r.User)
                    .Include(r => r.Images)
                    .FirstOrDefaultAsync(r => r.Id == rating.Id);

                return Ok(new
                {
                    success = true,
                    message = "Đánh giá thành công",
                    data = new
                    {
                        savedRating!.Id,
                        savedRating.Star,
                        savedRating.Comment,
                        savedRating.ReviewDate,
                        savedRating.LikesCount,
                        User = new
                        {
                            savedRating.User.Id,
                            savedRating.User.UserName,
                            savedRating.User.FullName,
                            savedRating.User.ProfileImageUrl
                        },
                        Images = savedRating.Images.Select(i => new { i.Id, i.ImageUrl, i.UploadedAt }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi gửi đánh giá", error = ex.Message });
            }
        }

        // POST: api/ProductApi/replies
        /// <summary>
        /// Gửi trả lời cho đánh giá
        /// </summary>
        [HttpPost("replies")]
        public async Task<IActionResult> SubmitReply([FromForm] int ratingId, [FromForm] string comment, [FromForm] IFormFileCollection? replyImages)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để trả lời" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrWhiteSpace(comment))
                {
                    return BadRequest(new { success = false, message = "Nội dung trả lời không được để trống" });
                }

                var rating = await _context.Ratings.FindAsync(ratingId);
                if (rating == null)
                {
                    return NotFound(new { success = false, message = "Đánh giá không tồn tại" });
                }

                var reply = new Reply
                {
                    RatingId = ratingId,
                    Comment = comment,
                    ReplyDate = DateTime.Now,
                    UserId = userId!,
                    LastModifiedBy = userId!,
                    IsVisible = true
                };

                _context.Replies.Add(reply);
                await _context.SaveChangesAsync();

                // Xử lý upload ảnh
                if (replyImages != null && replyImages.Count > 0)
                {
                    var uploads = Path.Combine(_webHostEnvironment.WebRootPath, "images", "replies");
                    if (!Directory.Exists(uploads))
                    {
                        Directory.CreateDirectory(uploads);
                    }

                    foreach (var file in replyImages)
                    {
                        if (file != null && file.Length > 0)
                        {
                            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                            
                            if (!allowedExtensions.Contains(ext))
                            {
                                continue;
                            }

                            var fileName = Guid.NewGuid().ToString() + ext;
                            var filePath = Path.Combine(uploads, fileName);
                            
                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }

                            var imageUrl = "/images/replies/" + fileName;
                            var replyImageEntity = new ReplyImage
                            {
                                ReplyId = reply.Id,
                                ImageUrl = imageUrl,
                                UploadedAt = DateTime.Now
                            };
                            _context.Add(replyImageEntity);
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                // Lấy lại reply với thông tin đầy đủ
                var savedReply = await _context.Replies
                    .Include(r => r.User)
                    .Include(r => r.Images)
                    .FirstOrDefaultAsync(r => r.Id == reply.Id);

                return Ok(new
                {
                    success = true,
                    message = "Trả lời thành công",
                    data = new
                    {
                        savedReply!.Id,
                        savedReply.RatingId,
                        savedReply.Comment,
                        savedReply.ReplyDate,
                        savedReply.LikesCount,
                        User = new
                        {
                            savedReply.User.Id,
                            savedReply.User.UserName,
                            savedReply.User.FullName,
                            savedReply.User.ProfileImageUrl
                        },
                        Images = savedReply.Images.Select(i => new { i.Id, i.ImageUrl, i.UploadedAt }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi gửi trả lời", error = ex.Message });
            }
        }

        // POST: api/ProductApi/reports
        /// <summary>
        /// Gửi báo cáo đánh giá vi phạm
        /// </summary>
        [HttpPost("reports")]
        public async Task<IActionResult> SubmitReport([FromBody] ReportRequest request)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để báo cáo" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return BadRequest(new { success = false, message = "Lý do báo cáo không được để trống" });
                }

                var rating = await _context.Ratings.FindAsync(request.RatingId);
                if (rating == null)
                {
                    return NotFound(new { success = false, message = "Đánh giá không tồn tại" });
                }

                // Kiểm tra đã báo cáo chưa
                var existingReport = await _context.Reports
                    .FirstOrDefaultAsync(r => r.RatingId == request.RatingId && r.ReporterId == userId);

                if (existingReport != null)
                {
                    return BadRequest(new { success = false, message = "Bạn đã báo cáo đánh giá này rồi" });
                }

                var report = new Report
                {
                    RatingId = request.RatingId,
                    ReporterId = userId!,
                    Reason = request.Reason,
                    ReportDate = DateTime.Now,
                    IsResolved = false
                };

                _context.Reports.Add(report);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Báo cáo thành công",
                    data = new
                    {
                        report.Id,
                        report.RatingId,
                        report.Reason,
                        report.ReportDate
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi gửi báo cáo", error = ex.Message });
            }
        }

        // POST: api/ProductApi/ratings/{id}/like
        /// <summary>
        /// Like đánh giá
        /// </summary>
        [HttpPost("ratings/{id}/like")]
        public async Task<IActionResult> LikeRating(int id)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để thích" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var rating = await _context.Ratings
                    .Include(r => r.UserLikes)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (rating == null)
                {
                    return NotFound(new { success = false, message = "Đánh giá không tồn tại" });
                }

                // Kiểm tra đã like chưa
                var existingLike = rating.UserLikes.FirstOrDefault(ul => ul.UserId == userId);
                if (existingLike != null)
                {
                    return BadRequest(new { success = false, message = "Bạn đã thích đánh giá này rồi" });
                }

                rating.UserLikes.Add(new UserLike { UserId = userId!, RatingId = id });
                rating.LikesCount++;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã thích đánh giá",
                    data = new { likesCount = rating.LikesCount }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi thích đánh giá", error = ex.Message });
            }
        }

        // DELETE: api/ProductApi/ratings/{id}/like
        /// <summary>
        /// Unlike đánh giá
        /// </summary>
        [HttpDelete("ratings/{id}/like")]
        public async Task<IActionResult> UnlikeRating(int id)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var rating = await _context.Ratings
                    .Include(r => r.UserLikes)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (rating == null)
                {
                    return NotFound(new { success = false, message = "Đánh giá không tồn tại" });
                }

                var like = rating.UserLikes.FirstOrDefault(ul => ul.UserId == userId);
                if (like == null)
                {
                    return BadRequest(new { success = false, message = "Bạn chưa thích đánh giá này" });
                }

                rating.UserLikes.Remove(like);
                rating.LikesCount = Math.Max(0, rating.LikesCount - 1);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã bỏ thích đánh giá",
                    data = new { likesCount = rating.LikesCount }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi bỏ thích đánh giá", error = ex.Message });
            }
        }

        // POST: api/ProductApi/replies/{id}/like
        /// <summary>
        /// Like reply
        /// </summary>
        [HttpPost("replies/{id}/like")]
        public async Task<IActionResult> LikeReply(int id)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để thích" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var reply = await _context.Replies
                    .Include(r => r.UserLikes)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (reply == null)
                {
                    return NotFound(new { success = false, message = "Phản hồi không tồn tại" });
                }

                // Kiểm tra đã like chưa
                var existingLike = reply.UserLikes.FirstOrDefault(ul => ul.UserId == userId);
                if (existingLike != null)
                {
                    return BadRequest(new { success = false, message = "Bạn đã thích phản hồi này rồi" });
                }

                reply.UserLikes.Add(new UserLike { UserId = userId!, ReplyId = id });
                reply.LikesCount++;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã thích phản hồi",
                    data = new { likesCount = reply.LikesCount }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi thích phản hồi", error = ex.Message });
            }
        }

        // DELETE: api/ProductApi/replies/{id}/like
        /// <summary>
        /// Unlike reply
        /// </summary>
        [HttpDelete("replies/{id}/like")]
        public async Task<IActionResult> UnlikeReply(int id)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var reply = await _context.Replies
                    .Include(r => r.UserLikes)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (reply == null)
                {
                    return NotFound(new { success = false, message = "Phản hồi không tồn tại" });
                }

                var like = reply.UserLikes.FirstOrDefault(ul => ul.UserId == userId);
                if (like == null)
                {
                    return BadRequest(new { success = false, message = "Bạn chưa thích phản hồi này" });
                }

                reply.UserLikes.Remove(like);
                reply.LikesCount = Math.Max(0, reply.LikesCount - 1);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Đã bỏ thích phản hồi",
                    data = new { likesCount = reply.LikesCount }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi bỏ thích phản hồi", error = ex.Message });
            }
        }

        // DELETE: api/ProductApi/ratings/{id}
        /// <summary>
        /// Xóa đánh giá của chính mình
        /// </summary>
        [HttpDelete("ratings/{id}")]
        public async Task<IActionResult> DeleteRating(int id)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var rating = await _context.Ratings
                    .Include(r => r.Images)
                    .Include(r => r.Replies)
                        .ThenInclude(rep => rep.Images)
                    .Include(r => r.UserLikes)
                    .Include(r => r.Reports)
                    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (rating == null)
                {
                    return NotFound(new { success = false, message = "Đánh giá không tồn tại hoặc bạn không có quyền xóa" });
                }

                // Xóa các file ảnh rating
                foreach (var image in rating.Images)
                {
                    var imagePath = Path.Combine(_webHostEnvironment.WebRootPath, image.ImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                // Xóa các file ảnh reply
                foreach (var reply in rating.Replies)
                {
                    foreach (var image in reply.Images)
                    {
                        var imagePath = Path.Combine(_webHostEnvironment.WebRootPath, image.ImageUrl.TrimStart('/'));
                        if (System.IO.File.Exists(imagePath))
                        {
                            System.IO.File.Delete(imagePath);
                        }
                    }
                }

                _context.Ratings.Remove(rating);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Xóa đánh giá thành công" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi xóa đánh giá", error = ex.Message });
            }
        }

        // DELETE: api/ProductApi/replies/{id}
        /// <summary>
        /// Xóa reply của chính mình
        /// </summary>
        [HttpDelete("replies/{id}")]
        public async Task<IActionResult> DeleteReply(int id)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var reply = await _context.Replies
                    .Include(r => r.Images)
                    .Include(r => r.UserLikes)
                    .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (reply == null)
                {
                    return NotFound(new { success = false, message = "Phản hồi không tồn tại hoặc bạn không có quyền xóa" });
                }

                // Xóa các file ảnh
                foreach (var image in reply.Images)
                {
                    var imagePath = Path.Combine(_webHostEnvironment.WebRootPath, image.ImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                _context.Replies.Remove(reply);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Xóa phản hồi thành công" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi xóa phản hồi", error = ex.Message });
            }
        }

        // PUT: api/ProductApi/ratings/{id}
        /// <summary>
        /// Chỉnh sửa đánh giá của chính mình
        /// </summary>
        [HttpPut("ratings/{id}")]
        public async Task<IActionResult> UpdateRating(int id, [FromBody] UpdateRatingRequest request)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var rating = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (rating == null)
                {
                    return NotFound(new { success = false, message = "Đánh giá không tồn tại hoặc bạn không có quyền chỉnh sửa" });
                }

                if (request.Star.HasValue)
                {
                    if (request.Star.Value < 1 || request.Star.Value > 5)
                    {
                        return BadRequest(new { success = false, message = "Số sao phải từ 1 đến 5" });
                    }
                    rating.Star = request.Star.Value;
                }

                if (!string.IsNullOrWhiteSpace(request.Comment))
                {
                    rating.Comment = request.Comment;
                }

                rating.LastModifiedBy = userId!;
                rating.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật đánh giá thành công",
                    data = new
                    {
                        rating.Id,
                        rating.Star,
                        rating.Comment,
                        rating.LastModifiedDate
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi cập nhật đánh giá", error = ex.Message });
            }
        }

        // PUT: api/ProductApi/replies/{id}
        /// <summary>
        /// Chỉnh sửa reply của chính mình
        /// </summary>
        [HttpPut("replies/{id}")]
        public async Task<IActionResult> UpdateReply(int id, [FromBody] UpdateReplyRequest request)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var reply = await _context.Replies.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

                if (reply == null)
                {
                    return NotFound(new { success = false, message = "Phản hồi không tồn tại hoặc bạn không có quyền chỉnh sửa" });
                }

                if (string.IsNullOrWhiteSpace(request.Comment))
                {
                    return BadRequest(new { success = false, message = "Nội dung phản hồi không được để trống" });
                }

                reply.Comment = request.Comment;
                reply.LastModifiedBy = userId!;
                reply.LastModifiedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật phản hồi thành công",
                    data = new
                    {
                        reply.Id,
                        reply.Comment,
                        reply.LastModifiedDate
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi cập nhật phản hồi", error = ex.Message });
            }
        }

        // GET: api/ProductApi/products/{id}/ratings
        /// <summary>
        /// Lấy danh sách đánh giá của sản phẩm (có phân trang và filter)
        /// </summary>
        [HttpGet("products/{id}/ratings")]
        public async Task<IActionResult> GetProductRatings(
            int id,
            [FromQuery] int? starFilter,
            [FromQuery] bool? hasImages,
            [FromQuery] string sortBy = "newest",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var product = await _context.Products.FindAsync(id);
                if (product == null || !product.IsActive)
                {
                    return NotFound(new { success = false, message = "Sản phẩm không tồn tại" });
                }

                var ratingsQuery = _context.Ratings
                    .Include(r => r.User)
                    .Include(r => r.Images)
                    .Include(r => r.Replies)
                        .ThenInclude(reply => reply.User)
                    .Include(r => r.Replies)
                        .ThenInclude(reply => reply.Images)
                    .Include(r => r.UserLikes)
                    .Where(r => r.ProductId == id && r.IsVisible)
                    .AsSplitQuery();

                // Filter theo số sao
                if (starFilter.HasValue && starFilter.Value >= 1 && starFilter.Value <= 5)
                {
                    ratingsQuery = ratingsQuery.Where(r => r.Star == starFilter.Value);
                }

                // Filter theo có ảnh
                if (hasImages.HasValue && hasImages.Value)
                {
                    ratingsQuery = ratingsQuery.Where(r => r.Images.Any());
                }

                // Sắp xếp
                ratingsQuery = sortBy.ToLower() switch
                {
                    "oldest" => ratingsQuery.OrderByDescending(r => r.IsPinned).ThenBy(r => r.ReviewDate),
                    "most_liked" => ratingsQuery.OrderByDescending(r => r.IsPinned).ThenByDescending(r => r.LikesCount),
                    "highest_rating" => ratingsQuery.OrderByDescending(r => r.IsPinned).ThenByDescending(r => r.Star),
                    "lowest_rating" => ratingsQuery.OrderByDescending(r => r.IsPinned).ThenBy(r => r.Star),
                    _ => ratingsQuery.OrderByDescending(r => r.IsPinned).ThenByDescending(r => r.ReviewDate) // newest (default)
                };

                var totalRatings = await ratingsQuery.CountAsync();

                var ratings = await ratingsQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(r => new
                    {
                        r.Id,
                        r.Star,
                        r.Comment,
                        r.ReviewDate,
                        r.LikesCount,
                        r.IsVisible,
                        r.IsPinned, // Thêm trường ghim
                        User = new
                        {
                            r.User.Id,
                            r.User.UserName,
                            r.User.FullName,
                            r.User.ProfileImageUrl
                        },
                        Images = r.Images.Select(i => new { i.Id, i.ImageUrl, i.UploadedAt }).ToList(),
                        Replies = r.Replies.Where(rep => rep.IsVisible).Select(rep => new
                        {
                            rep.Id,
                            rep.Comment,
                            rep.ReplyDate,
                            rep.LikesCount,
                            rep.IsVisible,
                            User = _context.UserRoles.Any(ur => ur.UserId == rep.User.Id && 
                                _context.Roles.Any(role => role.Id == ur.RoleId && (role.Name == "Admin" || role.Name == "Manager" || role.Name == "Staff")))
                                ? new
                                {
                                    rep.User.Id,
                                    UserName = (string?)"bloomie_shop",
                                    FullName = "Bloomie Shop",
                                    ProfileImageUrl = (string?)"/images/logos/bloomie_logo.png"
                                }
                                : new
                                {
                                    rep.User.Id,
                                    rep.User.UserName,
                                    rep.User.FullName,
                                    rep.User.ProfileImageUrl
                                },
                            Images = rep.Images.Select(i => new { i.Id, i.ImageUrl, i.UploadedAt }).ToList()
                        }).ToList(),
                        IsLiked = User.Identity!.IsAuthenticated && r.UserLikes.Any(ul => ul.UserId == User.FindFirstValue(ClaimTypes.NameIdentifier))
                    })
                    .ToListAsync();

                // Thống kê số lượng đánh giá theo sao
                var ratingStats = await _context.Ratings
                    .Where(r => r.ProductId == id && r.IsVisible)
                    .GroupBy(r => r.Star)
                    .Select(g => new { Star = g.Key, Count = g.Count() })
                    .ToListAsync();

                var averageRating = await _context.Ratings
                    .Where(r => r.ProductId == id && r.IsVisible)
                    .AverageAsync(r => (double?)r.Star) ?? 0;

                return Ok(new
                {
                    success = true,
                    data = ratings,
                    pagination = new
                    {
                        currentPage = page,
                        pageSize = pageSize,
                        totalItems = totalRatings,
                        totalPages = (int)Math.Ceiling(totalRatings / (double)pageSize)
                    },
                    statistics = new
                    {
                        averageRating = Math.Round(averageRating, 1),
                        totalRatings = totalRatings,
                        ratingDistribution = ratingStats.OrderByDescending(s => s.Star)
                    },
                    filters = new
                    {
                        starFilter,
                        hasImages,
                        sortBy
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách đánh giá", error = ex.Message });
            }
        }

        // GET: api/ProductApi/flower-types
        /// <summary>
        /// Lấy danh sách loại hoa
        /// </summary>
        [HttpGet("flower-types")]
        public async Task<IActionResult> GetFlowerTypes()
        {
            try
            {
                var flowerTypes = await _context.FlowerTypes
                    .Select(f => new
                    {
                        f.Id,
                        f.Name,
                        f.Description
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = flowerTypes });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách loại hoa", error = ex.Message });
            }
        }

        // GET: api/ProductApi/colors
        /// <summary>
        /// Lấy danh sách màu sắc có sẵn từ FlowerVariant
        /// </summary>
        [HttpGet("colors")]
        public async Task<IActionResult> GetAvailableColors()
        {
            try
            {
                var colors = await _context.FlowerVariants
                    .Where(fv => !string.IsNullOrEmpty(fv.Color))
                    .Select(fv => fv.Color)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToListAsync();

                return Ok(new { success = true, data = colors });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách màu sắc", error = ex.Message });
            }
        }

        // GET: api/ProductApi/shapes
        /// <summary>
        /// Lấy danh sách cách trình bày (Shape categories)
        /// </summary>
        [HttpGet("shapes")]
        public async Task<IActionResult> GetShapes()
        {
            try
            {
                // Lấy các category có Type = 2 (Shape)
                var shapes = await _context.Categories
                    .Where(c => c.Type == 2) // CategoryType.Shape = 2
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Description,
                        c.ParentId
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = shapes });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách cách trình bày", error = ex.Message });
            }
        }

        // ===== CHAT SUPPORT ENDPOINTS =====
        
        [HttpGet("search")]
        public async Task<IActionResult> SearchProducts([FromQuery] string q = "")
        {
            try
            {
                var now = DateTime.Now;
                var query = _context.Products
                    .Include(p => p.Images!)
                    .Include(p => p.ProductCategories!)
                        .ThenInclude(pc => pc.Category!)
                    .Where(p => p.IsActive);

                // Nếu có query thì lọc theo tên
                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(p => p.Name != null && p.Name.Contains(q));
                }

                var products = await query
                    .OrderByDescending(p => p.Id)
                    .Take(10)
                    .ToListAsync();

                // Lấy active discounts
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .OrderByDescending(d => d.Priority)
                    .ToListAsync();

                var result = products.Select(p => {
                    decimal? discountPrice = null;
                    
                    // Tìm discount áp dụng cho sản phẩm này
                    var discount = activeDiscounts.FirstOrDefault(d => 
                        d.ApplyTo == "all" ||
                        (d.ApplyTo == "products" && !string.IsNullOrEmpty(d.ProductIds) && d.ProductIds.Contains($"\"{p.Id}\"")) ||
                        (d.ApplyTo == "categories" && p.ProductCategories != null && p.ProductCategories.Any(pc => 
                            !string.IsNullOrEmpty(d.CategoryIds) && d.CategoryIds.Contains($"\"{pc.CategoryId}\"")))
                    );

                    if (discount != null)
                    {
                        if (discount.DiscountType == "percent")
                        {
                            var discountAmount = p.Price * discount.DiscountValue / 100;
                            if (discount.MaxDiscount.HasValue && discountAmount > discount.MaxDiscount.Value)
                                discountAmount = discount.MaxDiscount.Value;
                            discountPrice = p.Price - discountAmount;
                        }
                        else if (discount.DiscountType == "fixed_amount")
                        {
                            discountPrice = p.Price - discount.DiscountValue;
                            if (discountPrice < 0) discountPrice = 0;
                        }
                    }

                    return new
                    {
                        productId = p.Id,
                        productName = p.Name,
                        price = p.Price,
                        discountPrice = discountPrice,
                        imageUrl = p.Images != null && p.Images.Any() ? p.Images.First().Url : null,
                        categoryName = p.ProductCategories != null && p.ProductCategories.Any() 
                            ? p.ProductCategories.First().Category!.Name 
                            : null
                    };
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi tìm kiếm sản phẩm", error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProductById(int id)
        {
            try
            {
                var now = DateTime.Now;
                var product = await _context.Products
                    .Include(p => p.Images!)
                    .Include(p => p.ProductCategories!)
                        .ThenInclude(pc => pc.Category!)
                    .Where(p => p.Id == id)
                    .FirstOrDefaultAsync();

                if (product == null)
                {
                    return NotFound(new { message = "Không tìm thấy sản phẩm" });
                }

                // Tính discount
                decimal? discountPrice = null;
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .OrderByDescending(d => d.Priority)
                    .ToListAsync();

                var discount = activeDiscounts.FirstOrDefault(d => 
                    d.ApplyTo == "all" ||
                    (d.ApplyTo == "products" && !string.IsNullOrEmpty(d.ProductIds) && d.ProductIds.Contains($"\"{product.Id}\"")) ||
                    (d.ApplyTo == "categories" && product.ProductCategories != null && product.ProductCategories.Any(pc => 
                        !string.IsNullOrEmpty(d.CategoryIds) && d.CategoryIds.Contains($"\"{pc.CategoryId}\"")))
                );

                if (discount != null)
                {
                    if (discount.DiscountType == "percent")
                    {
                        var discountAmount = product.Price * discount.DiscountValue / 100;
                        if (discount.MaxDiscount.HasValue && discountAmount > discount.MaxDiscount.Value)
                            discountAmount = discount.MaxDiscount.Value;
                        discountPrice = product.Price - discountAmount;
                    }
                    else if (discount.DiscountType == "fixed_amount")
                    {
                        discountPrice = product.Price - discount.DiscountValue;
                        if (discountPrice < 0) discountPrice = 0;
                    }
                }

                var result = new
                {
                    productId = product.Id,
                    productName = product.Name,
                    price = product.Price,
                    discountPrice = discountPrice,
                    imageUrl = product.Images != null && product.Images.Any() ? product.Images.First().Url : null,
                    description = product.Description
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy thông tin sản phẩm", error = ex.Message });
            }
        }

        // POST: api/ProductApi/recently-viewed
        /// <summary>
        /// Lưu sản phẩm vừa xem cho người dùng đã đăng nhập
        /// </summary>
        [HttpPost("recently-viewed")]
        public async Task<IActionResult> SaveRecentlyViewed([FromBody] SaveRecentlyViewedRequest request)
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                // Kiểm tra sản phẩm có tồn tại không
                var product = await _context.Products.FindAsync(request.ProductId);
                if (product == null || !product.IsActive)
                {
                    return NotFound(new { success = false, message = "Sản phẩm không tồn tại" });
                }

                // Kiểm tra đã xem sản phẩm này chưa
                var existingView = await _context.RecentlyViewedProducts
                    .FirstOrDefaultAsync(rv => rv.UserId == userId && rv.ProductId == request.ProductId);

                if (existingView != null)
                {
                    // Cập nhật thời gian xem
                    existingView.ViewedAt = DateTime.Now;
                }
                else
                {
                    // Thêm mới
                    var recentlyViewed = new RecentlyViewed
                    {
                        UserId = userId!,
                        ProductId = request.ProductId,
                        ViewedAt = DateTime.Now
                    };
                    _context.RecentlyViewedProducts.Add(recentlyViewed);

                    // Xóa các sản phẩm cũ nếu vượt quá 16 sản phẩm
                    var userViewedCount = await _context.RecentlyViewedProducts
                        .CountAsync(rv => rv.UserId == userId);

                    if (userViewedCount >= 16)
                    {
                        var oldestViews = await _context.RecentlyViewedProducts
                            .Where(rv => rv.UserId == userId)
                            .OrderBy(rv => rv.ViewedAt)
                            .Take(userViewedCount - 15) // Giữ lại 15, thêm 1 mới = 16
                            .ToListAsync();

                        _context.RecentlyViewedProducts.RemoveRange(oldestViews);
                    }
                }

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Lưu lịch sử xem thành công" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lưu lịch sử xem", error = ex.Message });
            }
        }

        // GET: api/ProductApi/recently-viewed
        /// <summary>
        /// Lấy danh sách sản phẩm đã xem gần đây (16 sản phẩm trong 24h)
        /// </summary>
        [HttpGet("recently-viewed")]
        public async Task<IActionResult> GetRecentlyViewed()
        {
            try
            {
                if (!User.Identity!.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var twentyFourHoursAgo = DateTime.Now.AddHours(-24);

                // Lấy danh sách sản phẩm đã xem trong 24h, giới hạn 16 sản phẩm
                var recentlyViewedProducts = await _context.RecentlyViewedProducts
                    .Include(rv => rv.Product)
                        .ThenInclude(p => p.Images!)
                    .Include(rv => rv.Product)
                        .ThenInclude(p => p.ProductCategories!)
                    .Where(rv => rv.UserId == userId && rv.ViewedAt >= twentyFourHoursAgo)
                    .OrderByDescending(rv => rv.ViewedAt)
                    .Take(16)
                    .ToListAsync();

                if (!recentlyViewedProducts.Any())
                {
                    return Ok(new { success = true, data = new List<object>() });
                }

                // Lấy productIds để tính discount, rating và sold
                var productIds = recentlyViewedProducts.Select(rv => rv.ProductId).ToList();

                // Lấy active discounts
                var now = DateTime.Now;
                var activeDiscounts = await _context.ProductDiscounts
                    .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                    .ToListAsync();

                // Tính rating
                var ratingsData = await _context.Ratings
                    .Where(r => productIds.Contains(r.ProductId) && r.IsVisible)
                    .GroupBy(r => r.ProductId)
                    .Select(g => new { ProductId = g.Key, AvgRating = g.Average(r => (double)r.Star), Count = g.Count() })
                    .ToListAsync();

                var ratingsDict = ratingsData.ToDictionary(r => r.ProductId, r => (r.AvgRating, r.Count));

                // Tính số lượng đã bán
                var soldData = await _context.OrderDetails
                    .Include(od => od.Order)
                    .Where(od => productIds.Contains(od.ProductId) && od.Order != null && od.Order.Status == "Hoàn thành")
                    .GroupBy(od => od.ProductId)
                    .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(od => od.Quantity) })
                    .ToListAsync();

                var soldDict = soldData.ToDictionary(s => s.ProductId, s => s.TotalSold);

                // Tính giảm giá và build response
                var result = recentlyViewedProducts.Select(rv => {
                    var p = rv.Product;
                    decimal discountAmount = 0;
                    string? discountType = null;
                    decimal? discountValue = null;

                    foreach (var discount in activeDiscounts)
                    {
                        bool isApplicable = false;

                        if (discount.ApplyTo == "all")
                        {
                            isApplicable = true;
                        }
                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                        {
                            var productIdsList = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                            if (productIdsList != null && productIdsList.Contains(p.Id))
                            {
                                isApplicable = true;
                            }
                        }
                        else if (discount.ApplyTo == "categories" && !string.IsNullOrEmpty(discount.CategoryIds))
                        {
                            var categoryIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.CategoryIds);
                            var productCategoryIds = p.ProductCategories?.Select(pc => pc.CategoryId).ToList() ?? new List<int>();
                            if (categoryIds != null && categoryIds.Any(cid => productCategoryIds.Contains(cid)))
                            {
                                isApplicable = true;
                            }
                        }

                        if (isApplicable)
                        {
                            decimal tempDiscount = 0;
                            if (discount.DiscountType == "percent")
                            {
                                tempDiscount = p.Price * (discount.DiscountValue / 100);
                                if (discount.MaxDiscount.HasValue && tempDiscount > discount.MaxDiscount.Value)
                                {
                                    tempDiscount = discount.MaxDiscount.Value;
                                }
                            }
                            else if (discount.DiscountType == "fixed_amount")
                            {
                                tempDiscount = discount.DiscountValue;
                            }

                            if (tempDiscount > discountAmount)
                            {
                                discountAmount = tempDiscount;
                                discountType = discount.DiscountType;
                                discountValue = discount.DiscountValue;
                            }
                        }
                    }

                    // Lấy thông tin rating và sold
                    var hasRating = ratingsDict.TryGetValue(p.Id, out var ratingInfo);
                    var avgRating = hasRating ? ratingInfo.AvgRating : 0;
                    var totalReviews = hasRating ? ratingInfo.Count : 0;
                    var totalSold = soldDict.TryGetValue(p.Id, out var sold) ? sold : 0;

                    // Sắp xếp ảnh: ảnh chính lên đầu
                    var imagesList = p.Images!.Select(i => new { i.Id, i.Url }).ToList();
                    var primaryImageUrl = p.ImageUrl;
                    if (!string.IsNullOrEmpty(primaryImageUrl))
                    {
                        imagesList = imagesList.Where(i => i.Url != primaryImageUrl).ToList();
                        imagesList.Insert(0, new { Id = 0, Url = primaryImageUrl });
                    }

                    return new
                    {
                        p.Id,
                        p.Name,
                        p.Price,
                        p.ImageUrl,
                        Images = imagesList,
                        DiscountAmount = discountAmount,
                        DiscountType = discountType,
                        DiscountValue = discountValue,
                        DiscountedPrice = p.Price - discountAmount,
                        HasDiscount = discountAmount > 0,
                        AverageRating = Math.Round(avgRating, 1),
                        TotalReviews = totalReviews,
                        TotalSold = totalSold,
                        ViewedAt = rv.ViewedAt
                    };
                }).ToList();

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy lịch sử xem", error = ex.Message });
            }
        }

        /// <summary>
        /// API lấy giá theo ID FlowerType (cho AI nhận diện Python)
        /// </summary>
        /// <summary>
        [HttpGet("flower-price-by-id")]
        public async Task<IActionResult> GetFlowerPriceById([FromQuery] int flowerTypeId)
        {
            try
            {
                // Tìm FlowerType theo ID
                var flowerType = await _context.FlowerTypes
                    .FirstOrDefaultAsync(ft => ft.Id == flowerTypeId);

                if (flowerType == null)
                    return NotFound(new { 
                        success = false,
                        message = $"Không tìm thấy loại hoa với ID {flowerTypeId}."
                    });

                // Lấy tất cả variants của loại hoa này
                var variants = await _context.FlowerVariants
                    .Where(fv => fv.FlowerTypeId == flowerTypeId && fv.Stock > 0)
                    .ToListAsync();

                if (!variants.Any())
                    return NotFound(new { 
                        success = false,
                        message = $"Loại hoa '{flowerType.Name}' hiện đang hết hàng." 
                    });

                // Lấy giá nhập gần nhất của từng variant
                var variantPrices = new List<decimal>();
                var variantDetails = new List<object>();

                foreach (var variant in variants)
                {
                    var latestPrice = await _context.PurchaseOrderDetails
                        .Where(pod => pod.FlowerVariantId == variant.Id)
                        .OrderByDescending(pod => pod.PurchaseOrder.OrderDate)
                        .Select(pod => pod.UnitPrice)
                        .FirstOrDefaultAsync();

                    if (latestPrice > 0)
                    {
                        variantPrices.Add(latestPrice);
                        variantDetails.Add(new
                        {
                            Id = variant.Id,
                            Name = variant.Name,
                            Color = variant.Color,
                            Size = variant.Size,
                            Price = latestPrice,
                            Stock = variant.Stock
                        });
                    }
                }

                if (!variantPrices.Any())
                    return NotFound(new { 
                        success = false,
                        message = $"Loại hoa '{flowerType.Name}' chưa có giá nhập kho." 
                    });

                var avgPrice = variantPrices.Average();
                var minPrice = variantPrices.Min();
                var maxPrice = variantPrices.Max();
                var totalStock = variants.Sum(v => v.Stock);

                return Ok(new { 
                    success = true,
                    data = new
                    {
                        FlowerTypeId = flowerType.Id,
                        FlowerTypeName = flowerType.Name,
                        PricePerStem = new
                        {
                            Min = minPrice,
                            Max = maxPrice,
                            Average = Math.Round(avgPrice, 0),
                            Recommended = minPrice
                        },
                        TotalStock = totalStock,
                        AvailableVariants = variantDetails.Count,
                        Variants = variantDetails.OrderBy(v => ((dynamic)v).Price).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    success = false,
                    message = "Lỗi server khi lấy giá loại hoa.", 
                    error = ex.Message 
                });
            }
        }
    }

    // Request models
    public class ReportRequest
    {
        public int RatingId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class UpdateRatingRequest
    {
        public int? Star { get; set; }
        public string? Comment { get; set; }
    }

    public class UpdateReplyRequest
    {
        public string Comment { get; set; } = string.Empty;
    }

    public class SaveRecentlyViewedRequest
    {
        public int ProductId { get; set; }
    }
}
