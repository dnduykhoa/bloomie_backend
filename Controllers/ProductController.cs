using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;

namespace Bloomie.Controllers
{
    public class ProductController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ProductController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Danh sách sản phẩm
        public async Task<IActionResult> Index(int? categoryId, string categoryName, string searchString, string priceRange, 
            decimal? customMinPrice, decimal? customMaxPrice,
            int? minRating, bool? hasPromotion, int? minSold,
            int? topicId, int? recipientId, int? shapeId, string color, string flowerType, int? flowerTypeId, string sortOrder)
        {
            // Nếu có categoryName thì convert sang categoryId
            if (!string.IsNullOrEmpty(categoryName) && !categoryId.HasValue)
            {
                var category = await _context.Categories.FirstOrDefaultAsync(c => c.Name == categoryName);
                if (category != null)
                {
                    categoryId = category.Id;
                }
            }
            
            // Xử lý priceRange để set minPrice và maxPrice
            decimal? minPrice = null;
            decimal? maxPrice = null;
            
            if (!string.IsNullOrEmpty(priceRange))
            {
                switch (priceRange)
                {
                    case "duoi250000":
                        maxPrice = 250000;
                        break;
                    case "250000-500000":
                        minPrice = 250000;
                        maxPrice = 500000;
                        break;
                    case "500000-1000000":
                        minPrice = 500000;
                        maxPrice = 1000000;
                        break;
                    case "1000000-2000000":
                        minPrice = 1000000;
                        maxPrice = 2000000;
                        break;
                    case "tren2000000":
                        minPrice = 2000000;
                        break;
                }
            }
            
            // Nếu có custom price range, override priceRange
            if (customMinPrice.HasValue || customMaxPrice.HasValue)
            {
                minPrice = customMinPrice;
                maxPrice = customMaxPrice;
                priceRange = null; // Clear priceRange để custom price được ưu tiên
            }
            
            var productsQuery = _context.Products
                .Include(p => p.Images!)
                .Include(p => p.ProductCategories!)
                    .ThenInclude(pc => pc.Category!)
                .Include(p => p.ProductDetails!)
                    .ThenInclude(pd => pd.FlowerVariant!)
                        .ThenInclude(fv => fv.FlowerType!)
                .Where(p => p.IsActive);

            // Xác định filter đặc biệt
            string specialFilterParent = null;
            string specialFilterChild = null;
            var colorQuery = Request.Query["color"].ToString();
            var flowerTypeIdStr = Request.Query["flowerTypeId"].ToString();
            
            // Ưu tiên topicId, recipientId, shapeId trước
            if (topicId.HasValue)
            {
                var topicCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == topicId.Value);
                specialFilterParent = "Chủ đề";
                specialFilterChild = topicCategory?.Name ?? "";
            }
            else if (recipientId.HasValue)
            {
                var recipientCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == recipientId.Value);
                specialFilterParent = "Đối tượng";
                specialFilterChild = recipientCategory?.Name ?? "";
            }
            else if (shapeId.HasValue)
            {
                var shapeCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == shapeId.Value);
                specialFilterParent = "Cách trình bày";
                specialFilterChild = shapeCategory?.Name ?? "";
            }
            else if (Request.Query.ContainsKey("color"))
            {
                specialFilterParent = "Màu sắc";
                if (!string.IsNullOrEmpty(colorQuery)) specialFilterChild = colorQuery;
            }
            else if (Request.Query.ContainsKey("flowerTypeId"))
            {
                specialFilterParent = "Hoa tươi";
                if (!string.IsNullOrEmpty(flowerTypeIdStr) && int.TryParse(flowerTypeIdStr, out int parsedFlowerTypeId))
                {
                    var flowerTypeEntity = await _context.FlowerTypes.FirstOrDefaultAsync(f => f.Id == parsedFlowerTypeId);
                    if (flowerTypeEntity != null)
                        specialFilterChild = flowerTypeEntity.Name;
                    else
                        specialFilterChild = flowerTypeIdStr;
                }
            }

            if (categoryId.HasValue)
            {
                productsQuery = productsQuery.Where(p => p.ProductCategories != null && p.ProductCategories.Any(pc => pc.CategoryId == categoryId.Value));
            }
            
            // Filter by Topic (Chủ đề)
            if (topicId.HasValue)
            {
                productsQuery = productsQuery.Where(p => p.ProductCategories != null && 
                    p.ProductCategories.Any(pc => pc.CategoryId == topicId.Value));
            }
            
            // Filter by Recipient (Đối tượng)
            if (recipientId.HasValue)
            {
                productsQuery = productsQuery.Where(p => p.ProductCategories != null && 
                    p.ProductCategories.Any(pc => pc.CategoryId == recipientId.Value));
            }
            
            // Filter by Shape (Kiểu dáng)
            if (shapeId.HasValue)
            {
                productsQuery = productsQuery.Where(p => p.ProductCategories != null && 
                    p.ProductCategories.Any(pc => pc.CategoryId == shapeId.Value));
            }
            
            if (!string.IsNullOrEmpty(searchString))
            {
                productsQuery = productsQuery.Where(p => p.Name != null && p.Name.Contains(searchString));
            }
            if (minPrice.HasValue)
            {
                productsQuery = productsQuery.Where(p => p.Price >= minPrice.Value);
            }
            if (maxPrice.HasValue)
            {
                productsQuery = productsQuery.Where(p => p.Price <= maxPrice.Value);
            }
            
            // Filter by Color (via ProductDetails -> FlowerVariant -> Color)
            if (!string.IsNullOrEmpty(color))
            {
                productsQuery = productsQuery.Where(p => p.ProductDetails != null && 
                    p.ProductDetails.Any(pd => pd.FlowerVariant != null && 
                        pd.FlowerVariant.Color != null && 
                        pd.FlowerVariant.Color.Contains(color)));
            }
            
            // Filter by FlowerType (via ProductDetails -> FlowerVariant -> FlowerType)
            // Hỗ trợ filter theo ID hoặc tên
            if (flowerTypeId.HasValue)
            {
                productsQuery = productsQuery.Where(p => p.ProductDetails != null && 
                    p.ProductDetails.Any(pd => pd.FlowerVariant != null && 
                        pd.FlowerVariant.FlowerTypeId == flowerTypeId.Value));
            }
            else if (!string.IsNullOrEmpty(flowerType))
            {
                productsQuery = productsQuery.Where(p => p.ProductDetails != null && 
                    p.ProductDetails.Any(pd => pd.FlowerVariant != null && 
                        pd.FlowerVariant.FlowerType != null && 
                        pd.FlowerVariant.FlowerType.Name != null && 
                        pd.FlowerVariant.FlowerType.Name.Contains(flowerType)));
            }

            var products = await productsQuery.ToListAsync();

            // Lấy tất cả ProductDiscount đang active
            var now = DateTime.Now;
            var activeDiscounts = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .ToListAsync();

            // Tạo dictionary để lưu giá giảm cho từng sản phẩm
            var productDiscountPrices = new Dictionary<int, (decimal originalPrice, decimal discountedPrice, string discountType, decimal discountValue)>();
            
            foreach (var discount in activeDiscounts)
            {
                // Parse ProductIds JSON string to list
                if (!string.IsNullOrEmpty(discount.ProductIds))
                {
                    try
                    {
                        var productIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                        if (productIds != null)
                        {
                            foreach (var productId in productIds)
                            {
                                var product = products.FirstOrDefault(p => p.Id == productId);
                                if (product != null)
                                {
                                    decimal discountedPrice = product.Price;
                                    
                                    if (discount.DiscountType == "percent")
                                    {
                                        discountedPrice = product.Price * (1 - discount.DiscountValue / 100);
                                    }
                                    else if (discount.DiscountType == "fixed_amount")
                                    {
                                        discountedPrice = product.Price - discount.DiscountValue;
                                    }
                                    
                                    // Đảm bảo giá không âm
                                    if (discountedPrice < 0) discountedPrice = 0;
                                    
                                    // Chỉ lưu nếu chưa có hoặc discount này tốt hơn
                                    if (!productDiscountPrices.ContainsKey(product.Id) || 
                                        discountedPrice < productDiscountPrices[product.Id].discountedPrice)
                                    {
                                        productDiscountPrices[product.Id] = (product.Price, discountedPrice, discount.DiscountType, discount.DiscountValue);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip nếu không parse được JSON
                    }
                }
                // Nếu ApplyTo = "all", áp dụng cho tất cả sản phẩm
                else if (discount.ApplyTo == "all")
                {
                    foreach (var product in products)
                    {
                        decimal discountedPrice = product.Price;
                        
                        if (discount.DiscountType == "percent")
                        {
                            discountedPrice = product.Price * (1 - discount.DiscountValue / 100);
                        }
                        else if (discount.DiscountType == "fixed_amount")
                        {
                            discountedPrice = product.Price - discount.DiscountValue;
                        }
                        
                        if (discountedPrice < 0) discountedPrice = 0;
                        
                        if (!productDiscountPrices.ContainsKey(product.Id) || 
                            discountedPrice < productDiscountPrices[product.Id].discountedPrice)
                        {
                            productDiscountPrices[product.Id] = (product.Price, discountedPrice, discount.DiscountType, discount.DiscountValue);
                        }
                    }
                }
            }
            
            // Truyền vào ViewBag để sử dụng trong View
            ViewBag.ProductDiscountPrices = productDiscountPrices;

            // Tính rating trung bình và số lượng đã bán cho mỗi sản phẩm
            var productListItems = new List<Bloomie.Models.ViewModels.ProductListItemViewModel>();
            
            foreach (var product in products)
            {
                // Tính rating trung bình
                var ratings = await _context.Ratings
                    .Where(r => r.ProductId == product.Id && r.IsVisible)
                    .ToListAsync();
                
                var avgRating = ratings.Any() ? ratings.Average(r => r.Star) : 0;
                var totalReviews = ratings.Count;
                
                // Tính tổng số lượng đã bán (từ các đơn hàng đã hoàn thành)
                var totalSold = await _context.OrderDetails
                    .Include(od => od.Order)
                    .Where(od => od.ProductId == product.Id && 
                                 od.Order != null && 
                                 od.Order.Status == "Hoàn thành")
                    .SumAsync(od => (int?)od.Quantity) ?? 0;
                
                // Check if product has promotion
                var hasPromotionValue = productDiscountPrices.ContainsKey(product.Id);
                
                productListItems.Add(new Bloomie.Models.ViewModels.ProductListItemViewModel
                {
                    Product = product,
                    AverageRating = avgRating,
                    TotalReviews = totalReviews,
                    TotalSold = totalSold,
                    HasPromotion = hasPromotionValue
                });
            }

            // Apply filters on calculated data
            if (minRating.HasValue && minRating.Value > 0)
            {
                productListItems = productListItems.Where(p => p.AverageRating >= minRating.Value).ToList();
            }
            
            if (hasPromotion.HasValue && hasPromotion.Value)
            {
                productListItems = productListItems.Where(p => p.HasPromotion).ToList();
            }
            
            if (minSold.HasValue && minSold.Value > 0)
            {
                productListItems = productListItems.Where(p => p.TotalSold >= minSold.Value).ToList();
            }

            // Apply sorting
            switch (sortOrder)
            {
                case "price_asc":
                    productListItems = productListItems.OrderBy(p => p.Product.Price).ToList();
                    break;
                case "price_desc":
                    productListItems = productListItems.OrderByDescending(p => p.Product.Price).ToList();
                    break;
                case "newest":
                    productListItems = productListItems.OrderByDescending(p => p.Product.Id).ToList();
                    break;
                default:
                    // Mặc định: sắp xếp theo Id giảm dần (mới nhất)
                    productListItems = productListItems.OrderByDescending(p => p.Product.Id).ToList();
                    break;
            }

            // Lấy danh mục để filter
            var categories = await _context.Categories.ToListAsync();
            
            // Group categories by Type
            var topicCategories = categories.Where(c => c.Type == (int)CategoryType.Topic).ToList();
            var recipientCategories = categories.Where(c => c.Type == (int)CategoryType.Recipient).ToList();
            var shapeCategories = categories.Where(c => c.Type == (int)CategoryType.Shape).ToList();
            
            // Get distinct colors directly from FlowerVariants table
            var colors = await _context.FlowerVariants
                .Where(fv => !string.IsNullOrEmpty(fv.Color))
                .Select(fv => fv.Color!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
            
            // Get all flower types directly from FlowerTypes table
            var flowerTypesList = await _context.FlowerTypes
                .Where(ft => !string.IsNullOrEmpty(ft.Name))
                .OrderBy(ft => ft.Name)
                .ToListAsync();
            
            // Tạo list tên flower types (để backward compatible)
            var flowerTypes = flowerTypesList.Select(ft => ft.Name).ToList();
            
            ViewBag.Categories = categories;
            ViewBag.TopicCategories = topicCategories;
            ViewBag.RecipientCategories = recipientCategories;
            ViewBag.ShapeCategories = shapeCategories;
            ViewBag.Colors = colors;
            ViewBag.FlowerTypes = flowerTypes;
            ViewBag.FlowerTypesList = flowerTypesList; // List đầy đủ với ID
            
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.SelectedTopicId = topicId;
            ViewBag.SelectedRecipientId = recipientId;
            ViewBag.SelectedShapeId = shapeId;
            ViewBag.SelectedColor = color;
            ViewBag.SelectedFlowerType = flowerType;
            ViewBag.SelectedFlowerTypeId = flowerTypeId;
            
            ViewBag.SearchString = searchString;
            ViewBag.SortOrder = sortOrder;
            ViewBag.PriceRange = priceRange;
            ViewBag.CustomMinPrice = customMinPrice;
            ViewBag.CustomMaxPrice = customMaxPrice;
            ViewBag.MinRating = minRating;
            ViewBag.HasPromotion = hasPromotion;
            ViewBag.MinSold = minSold;
            
            // Breadcrumb information
            Category? selectedCategory = null;
            Category? parentCategory = null;
            
            if (categoryId.HasValue)
            {
                selectedCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == categoryId.Value);
                if (selectedCategory != null)
                {
                    // Set breadcrumb based on category type
                    if (selectedCategory.Type == 0) // Topic
                    {
                        specialFilterParent = "Chủ đề";
                        specialFilterChild = selectedCategory.Name;
                    }
                    else if (selectedCategory.Type == 1) // Recipient
                    {
                        specialFilterParent = "Đối tượng";
                        specialFilterChild = selectedCategory.Name;
                    }
                    else if (selectedCategory.Type == 2) // Shape
                    {
                        specialFilterParent = "Cách trình bày";
                        specialFilterChild = selectedCategory.Name;
                    }
                    
                    if (selectedCategory.ParentId.HasValue)
                    {
                        parentCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == selectedCategory.ParentId.Value);
                    }
                }
            }
            else if (topicId.HasValue)
            {
                selectedCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == topicId.Value);
            }
            else if (recipientId.HasValue)
            {
                selectedCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == recipientId.Value);
            }
            else if (shapeId.HasValue)
            {
                selectedCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == shapeId.Value);
            }
            
            ViewBag.SelectedCategory = selectedCategory;
            ViewBag.ParentCategory = parentCategory;
            ViewBag.SpecialFilterParent = specialFilterParent;
            ViewBag.SpecialFilterChild = specialFilterChild;
            
            // Nếu có phân trang thì tính toán, ở đây mặc định là false
            ViewBag.HasMoreProducts = false;

            return View(productListItems);
        }

        // Chi tiết sản phẩm
        public async Task<IActionResult> Details(int id)
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
                return NotFound();
            }

            // Lấy đánh giá và trả lời kèm ảnh
            var ratings = await _context.Ratings
                .Include(r => r.User)
                .Include(r => r.Images)
                .Include(r => r.Replies)
                .ThenInclude(reply => reply.User)
                .Include(r => r.Replies)
                .ThenInclude(reply => reply.Images)
                .Where(r => r.ProductId == id && r.IsVisible)
                .OrderByDescending(r => r.IsPinned) // Đánh giá được ghim lên đầu
                .ThenByDescending(r => r.ReviewDate) // Sau đó sắp xếp theo thời gian
                .AsSplitQuery() // Thêm AsSplitQuery để EF load navigation property đúng
                .ToListAsync();
            ViewBag.Ratings = ratings;
            ViewBag.AverageRating = ratings.Any() ? ratings.Average(r => r.Star) : 0;

            // Kiểm tra các đánh giá mà user hiện tại đã báo cáo
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var reportedRatingIds = await _context.Reports
                    .Where(r => r.ReporterId == userId)
                    .Select(r => r.RatingId)
                    .ToListAsync();
                ViewBag.ReportedRatingIds = reportedRatingIds;
            }
            else
            {
                ViewBag.ReportedRatingIds = new List<int>();
            }

            // Breadcrumb động: lấy filter đặc biệt từ query string
            string? specialFilterParent = null;
            string? specialFilterChild = null;
            var color = Request.Query["color"].ToString();
            var flowerTypeIdStr = Request.Query["flowerTypeId"].ToString();
            var topicIdStr = Request.Query["topicId"].ToString();
            var recipientIdStr = Request.Query["recipientId"].ToString();
            var shapeIdStr = Request.Query["shapeId"].ToString();
            
            if (Request.Query.ContainsKey("topicId") && !string.IsNullOrEmpty(topicIdStr) && int.TryParse(topicIdStr, out int topicId))
            {
                var topicCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == topicId && c.Type == 0); // Topic = 0
                if (topicCategory != null)
                {
                    specialFilterParent = "Chủ đề";
                    specialFilterChild = topicCategory.Name;
                }
            }
            else if (Request.Query.ContainsKey("recipientId") && !string.IsNullOrEmpty(recipientIdStr) && int.TryParse(recipientIdStr, out int recipientId))
            {
                var recipientCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == recipientId && c.Type == 1); // Recipient = 1
                if (recipientCategory != null)
                {
                    specialFilterParent = "Đối tượng";
                    specialFilterChild = recipientCategory.Name;
                }
            }
            else if (Request.Query.ContainsKey("shapeId") && !string.IsNullOrEmpty(shapeIdStr) && int.TryParse(shapeIdStr, out int shapeId))
            {
                var shapeCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == shapeId && c.Type == 2); // Shape = 2
                if (shapeCategory != null)
                {
                    specialFilterParent = "Cách trình bày";
                    specialFilterChild = shapeCategory.Name;
                }
            }
            else if (Request.Query.ContainsKey("color"))
            {
                specialFilterParent = "Màu sắc";
                if (!string.IsNullOrEmpty(color)) specialFilterChild = color;
            }
            else if (Request.Query.ContainsKey("flowerTypeId"))
            {
                specialFilterParent = "Hoa tươi";
                if (!string.IsNullOrEmpty(flowerTypeIdStr) && int.TryParse(flowerTypeIdStr, out int flowerTypeId))
                {
                    var flowerTypeEntity = await _context.FlowerTypes.FirstOrDefaultAsync(f => f.Id == flowerTypeId);
                    if (flowerTypeEntity != null && flowerTypeEntity.Name != null)
                        specialFilterChild = flowerTypeEntity.Name;
                    else
                        specialFilterChild = flowerTypeIdStr;
                }
            }
            ViewBag.SpecialFilterParent = specialFilterParent;
            ViewBag.SpecialFilterChild = specialFilterChild;

            // Breadcrumb động: lấy category cha/con nếu có
            var productCategory = product.ProductCategories?.FirstOrDefault();
            if (productCategory != null)
            {
                var subCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == productCategory.CategoryId);
                ViewBag.SubCategory = subCategory;
                if (subCategory != null && subCategory.ParentId.HasValue)
                {
                    var parentCategory = await _context.Categories.FirstOrDefaultAsync(c => c.Id == subCategory.ParentId.Value);
                    ViewBag.ParentCategory = parentCategory;
                }
            }

            // Tính giảm giá sản phẩm
            var now = DateTime.Now;
            var activeDiscounts = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .ToListAsync();

            decimal productDiscountAmount = 0;
            string productDiscountType = "";
            decimal productDiscountValue = 0;

            foreach (var discount in activeDiscounts)
            {
                bool isApplicable = false;

                if (discount.ApplyTo == "all")
                {
                    isApplicable = true;
                }
                else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                {
                    var productIds = JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                    if (productIds != null && productIds.Contains(product.Id))
                    {
                        isApplicable = true;
                    }
                }
                else if (discount.ApplyTo == "categories" && !string.IsNullOrEmpty(discount.CategoryIds))
                {
                    var categoryIds = JsonSerializer.Deserialize<List<int>>(discount.CategoryIds);
                    var productCategoryIds = product.ProductCategories?.Select(pc => pc.CategoryId).ToList() ?? new List<int>();
                    if (categoryIds != null && categoryIds.Any(cid => productCategoryIds.Contains(cid)))
                    {
                        isApplicable = true;
                    }
                }

                if (isApplicable)
                {
                    if (discount.DiscountType == "percent")
                    {
                        decimal discountAmount = product.Price * discount.DiscountValue / 100;
                        if (discountAmount > productDiscountAmount)
                        {
                            productDiscountAmount = discountAmount;
                            productDiscountType = "percent";
                            productDiscountValue = discount.DiscountValue;
                        }
                    }
                    else if (discount.DiscountType == "fixed_amount")
                    {
                        if (discount.DiscountValue > productDiscountAmount)
                        {
                            productDiscountAmount = discount.DiscountValue;
                            productDiscountType = "fixed_amount";
                            productDiscountValue = discount.DiscountValue;
                        }
                    }
                }
            }

            if (productDiscountAmount > 0)
            {
                ViewBag.ProductDiscount = new
                {
                    discountAmount = productDiscountAmount,
                    discountType = productDiscountType,
                    discountValue = productDiscountValue
                };
            }

            // Sản phẩm tương tự (gợi ý)
            var similarProducts = new List<Product>();
            if (product.ProductCategories != null && product.ProductCategories.Any())
            {
                var categoryIds = product.ProductCategories.Select(x => x.CategoryId).ToList();
                similarProducts = await _context.Products
                    .Include(p => p.Images)
                    .Where(p => p.Id != id && p.IsActive && p.ProductCategories != null && p.ProductCategories.Any(pc => categoryIds.Contains(pc.CategoryId)))
                    .Take(12)
                    .ToListAsync();
                    
                // Tính toán rating và sold cho mỗi sản phẩm
                var productIds = similarProducts.Select(p => p.Id).ToList();
                var ratingsData = await _context.Ratings
                    .Where(r => productIds.Contains(r.ProductId) && r.IsVisible)
                    .GroupBy(r => r.ProductId)
                    .Select(g => new { ProductId = g.Key, AvgRating = g.Average(r => (double)r.Star), Count = g.Count() })
                    .ToListAsync();
                    
                var soldData = await _context.OrderDetails
                    .Include(od => od.Order)
                    .Where(od => productIds.Contains(od.ProductId) && od.Order != null && od.Order.Status == "Hoàn thành")
                    .GroupBy(od => od.ProductId)
                    .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(od => od.Quantity) })
                    .ToListAsync();
                    
                ViewBag.RatingsData = ratingsData.ToDictionary(r => r.ProductId, r => (object)new { r.AvgRating, r.Count });
                ViewBag.SoldData = soldData.ToDictionary(s => s.ProductId, s => s.TotalSold);

                // Tính giảm giá cho sản phẩm tương tự
                var similarDiscountPrices = new Dictionary<int, (decimal discountAmount, string discountType, decimal discountValue)>();
                foreach (var similarProduct in similarProducts)
                {
                    decimal discountAmount = 0;
                    string discountType = "";
                    decimal discountValue = 0;

                    foreach (var discount in activeDiscounts)
                    {
                        bool isApplicable = false;

                        if (discount.ApplyTo == "all")
                        {
                            isApplicable = true;
                        }
                        else if (discount.ApplyTo == "products" && !string.IsNullOrEmpty(discount.ProductIds))
                        {
                            var discountProductIds = JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                            if (discountProductIds != null && discountProductIds.Contains(similarProduct.Id))
                            {
                                isApplicable = true;
                            }
                        }
                        else if (discount.ApplyTo == "categories" && !string.IsNullOrEmpty(discount.CategoryIds))
                        {
                            var discountCategoryIds = JsonSerializer.Deserialize<List<int>>(discount.CategoryIds);
                            var productCategoryIds = await _context.ProductCategories
                                .Where(pc => pc.ProductId == similarProduct.Id)
                                .Select(pc => pc.CategoryId)
                                .ToListAsync();
                            if (discountCategoryIds != null && discountCategoryIds.Any(cid => productCategoryIds.Contains(cid)))
                            {
                                isApplicable = true;
                            }
                        }

                        if (isApplicable)
                        {
                            if (discount.DiscountType == "percent")
                            {
                                decimal amount = similarProduct.Price * discount.DiscountValue / 100;
                                if (amount > discountAmount)
                                {
                                    discountAmount = amount;
                                    discountType = "percent";
                                    discountValue = discount.DiscountValue;
                                }
                            }
                            else if (discount.DiscountType == "fixed_amount")
                            {
                                if (discount.DiscountValue > discountAmount)
                                {
                                    discountAmount = discount.DiscountValue;
                                    discountType = "fixed_amount";
                                    discountValue = discount.DiscountValue;
                                }
                            }
                        }
                    }

                    if (discountAmount > 0)
                    {
                        similarDiscountPrices[similarProduct.Id] = (discountAmount, discountType, discountValue);
                    }
                }
                ViewBag.SimilarProductDiscountPrices = similarDiscountPrices;
            }
            else
            {
                ViewBag.RatingsData = new Dictionary<int, object>();
                ViewBag.SoldData = new Dictionary<int, int>();
            }
            ViewBag.SimilarProducts = similarProducts;

            return View(product);
        }

        // Gửi đánh giá sản phẩm
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitRating(int productId, int star, string comment, IFormFileCollection ratingImage)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var rating = new Rating
            {
                ProductId = productId,
                Star = star,
                Comment = comment,
                ReviewDate = DateTime.Now,
                UserId = userId,
                IsVisible = true,
                ImageUrl = "",
                LastModifiedBy = userId
            };
            _context.Ratings.Add(rating);
            await _context.SaveChangesAsync();

            // Xử lý upload nhiều ảnh
            if (ratingImage != null && ratingImage.Count > 0)
            {
                foreach (var file in ratingImage)
                {
                    if (file != null && file.Length > 0)
                    {
                        // Lưu file vào wwwroot/images/ratings
                        var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/ratings");
                        if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
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
            return RedirectToAction("Details", new { id = productId });
        }

        // Gửi trả lời cho đánh giá
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReply(int ratingId, string comment)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var reply = new Reply
            {
                RatingId = ratingId,
                Comment = comment,
                ReplyDate = DateTime.Now,
                UserId = userId,
                LastModifiedBy = userId,
                IsVisible = true
            };
            _context.Replies.Add(reply);
            await _context.SaveChangesAsync();

            // Xử lý upload nhiều ảnh cho reply
            var replyImages = Request.Form.Files;
            if (replyImages != null && replyImages.Count > 0)
            {
                var allowedExtensions = new List<string> { ".jpg", ".jpeg", ".png", ".gif" };
                foreach (var file in replyImages)
                {
                    if (file != null && file.Length > 0 && file.Name == "replyImages")
                    {
                        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                        if (!allowedExtensions.Contains(ext)) continue;
                        var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/replies");
                        if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);
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

            var rating = await _context.Ratings.Include(r => r.Product).FirstOrDefaultAsync(r => r.Id == ratingId);
            // Redirect về chi tiết sản phẩm và cuộn tới đánh giá vừa trả lời
            if (rating != null)
            {
                return Redirect($"/Product/Details/{rating.ProductId}#rating-{ratingId}");
            }
            return RedirectToAction("Details", new { id = rating?.ProductId });
        }

        // Gửi báo cáo đánh giá
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReport(int ratingId, string reason)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            
            // Kiểm tra xem user đã báo cáo đánh giá này chưa
            var existingReport = await _context.Reports
                .FirstOrDefaultAsync(r => r.RatingId == ratingId && r.ReporterId == userId);
            
            if (existingReport != null)
            {
                TempData["ErrorMessage"] = "Bạn đã báo cáo đánh giá này trước đó rồi!";
                var rating = await _context.Ratings.Include(r => r.Product).FirstOrDefaultAsync(r => r.Id == ratingId);
                return RedirectToAction("Details", new { id = rating?.ProductId });
            }
            
            var report = new Report
            {
                RatingId = ratingId,
                ReporterId = userId,
                Reason = reason,
                ReportDate = DateTime.Now,
                IsResolved = false
            };
            _context.Reports.Add(report);
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Báo cáo của bạn đã được gửi thành công!";
            var ratingResult = await _context.Ratings.Include(r => r.Product).FirstOrDefaultAsync(r => r.Id == ratingId);
            return RedirectToAction("Details", new { id = ratingResult?.ProductId });
        }

        // Like/Unlike đánh giá
        [HttpPost]
        public async Task<IActionResult> LikeRating(int itemId)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var rating = await _context.Ratings.Include(r => r.UserLikes).FirstOrDefaultAsync(r => r.Id == itemId);
            if (rating != null && !rating.UserLikes.Any(ul => ul.UserId == userId))
            {
                rating.UserLikes.Add(new UserLike { UserId = userId, RatingId = itemId });
                rating.LikesCount++;
                await _context.SaveChangesAsync();
            }
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> UnlikeRating(int itemId)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var rating = await _context.Ratings.Include(r => r.UserLikes).FirstOrDefaultAsync(r => r.Id == itemId);
            var like = rating?.UserLikes.FirstOrDefault(ul => ul.UserId == userId);
            if (like != null)
            {
                rating.UserLikes.Remove(like);
                rating.LikesCount = Math.Max(0, rating.LikesCount - 1);
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        // Like/Unlike reply
        [HttpPost]
        public async Task<IActionResult> LikeReply(int itemId)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var reply = await _context.Replies.Include(r => r.UserLikes).FirstOrDefaultAsync(r => r.Id == itemId);
            if (reply != null && !reply.UserLikes.Any(ul => ul.UserId == userId))
            {
                reply.UserLikes.Add(new UserLike { UserId = userId, ReplyId = itemId });
                reply.LikesCount++;
                await _context.SaveChangesAsync();
            }
            return Ok();
        }
        [HttpPost]
        public async Task<IActionResult> UnlikeReply(int itemId)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var reply = await _context.Replies.Include(r => r.UserLikes).FirstOrDefaultAsync(r => r.Id == itemId);
            var like = reply?.UserLikes.FirstOrDefault(ul => ul.UserId == userId);
            if (like != null)
            {
                reply.UserLikes.Remove(like);
                reply.LikesCount = Math.Max(0, reply.LikesCount - 1);
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        // Xóa đánh giá
        [HttpPost]
        public async Task<IActionResult> DeleteRating(int itemId)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var rating = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId);
            if (rating != null)
            {
                _context.Ratings.Remove(rating);
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        // Xóa trả lời
        [HttpPost]
        public async Task<IActionResult> DeleteReply(int itemId)
        {
            if (!User.Identity.IsAuthenticated) return Unauthorized();
            var userId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var reply = await _context.Replies.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId);
            if (reply != null)
            {
                _context.Replies.Remove(reply);
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        // Get products info for recently viewed (rating, sold count)
        [HttpGet]
        public async Task<IActionResult> GetProductsInfo([FromQuery] List<int> productIds)
        {
            if (productIds == null || !productIds.Any())
            {
                return Json(new List<object>());
            }

            var productsInfo = new List<object>();
            
            // Get active discounts
            var now = DateTime.Now;
            var activeDiscounts = await _context.ProductDiscounts
                .Where(d => d.IsActive && d.StartDate <= now && (d.EndDate == null || d.EndDate >= now))
                .ToListAsync();
            
            // Build discount map
            var productDiscountMap = new Dictionary<int, (decimal discountedPrice, string discountType, decimal discountValue)>();
            foreach (var discount in activeDiscounts)
            {
                if (!string.IsNullOrEmpty(discount.ProductIds))
                {
                    try
                    {
                        var discountProductIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(discount.ProductIds);
                        if (discountProductIds != null)
                        {
                            foreach (var pid in discountProductIds)
                            {
                                if (productIds.Contains(pid))
                                {
                                    var product = await _context.Products.FindAsync(pid);
                                    if (product != null)
                                    {
                                        decimal discountedPrice = product.Price;
                                        
                                        if (discount.DiscountType == "percent")
                                        {
                                            discountedPrice = product.Price * (1 - discount.DiscountValue / 100);
                                        }
                                        else if (discount.DiscountType == "fixed_amount")
                                        {
                                            discountedPrice = product.Price - discount.DiscountValue;
                                        }
                                        
                                        if (discountedPrice < 0) discountedPrice = 0;
                                        
                                        if (!productDiscountMap.ContainsKey(pid) || discountedPrice < productDiscountMap[pid].discountedPrice)
                                        {
                                            productDiscountMap[pid] = (discountedPrice, discount.DiscountType, discount.DiscountValue);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            
            foreach (var productId in productIds)
            {
                // Get product
                var product = await _context.Products.FindAsync(productId);
                if (product == null) continue;
                
                // Get rating info
                var ratings = await _context.Ratings
                    .Where(r => r.ProductId == productId && r.IsVisible)
                    .ToListAsync();
                
                var averageRating = ratings.Any() ? ratings.Average(r => (double)r.Star) : 0;
                var totalReviews = ratings.Count;
                
                // Get sold count
                var totalSold = await _context.OrderDetails
                    .Include(od => od.Order)
                    .Where(od => od.ProductId == productId && 
                                od.Order != null && 
                                od.Order.Status == "Hoàn thành")
                    .SumAsync(od => (int?)od.Quantity) ?? 0;
                
                // Get discount info
                decimal finalPrice = product.Price;
                string discountType = "";
                decimal discountValue = 0;
                if (productDiscountMap.ContainsKey(productId))
                {
                    finalPrice = productDiscountMap[productId].discountedPrice;
                    discountType = productDiscountMap[productId].discountType;
                    discountValue = productDiscountMap[productId].discountValue;
                }
                
                productsInfo.Add(new
                {
                    id = productId,
                    averageRating = averageRating,
                    totalReviews = totalReviews,
                    totalSold = totalSold,
                    finalPrice = finalPrice,
                    discountType = discountType,
                    discountValue = discountValue
                });
            }

            return Json(productsInfo);
        }
    }
}
