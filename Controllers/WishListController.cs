using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using System.Security.Claims;

namespace Bloomie.Controllers
{
    [Authorize]
    public class WishListController : Controller
    {
        private readonly ApplicationDbContext _context;

        public WishListController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: WishList
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var wishListItems = await _context.WishLists
                .Include(w => w.Product)
                    .ThenInclude(p => p.Images)
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.CreatedDate)
                .ToListAsync();

            var productListItems = new List<ProductListItemViewModel>();

            foreach (var item in wishListItems)
            {
                var product = item.Product;
                
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
                
                productListItems.Add(new ProductListItemViewModel
                {
                    Product = product,
                    AverageRating = avgRating,
                    TotalReviews = totalReviews,
                    TotalSold = totalSold,
                    HasPromotion = false
                });
            }

            return View(productListItems);
        }

        // POST: WishList/Add
        [HttpPost]
        public async Task<IActionResult> Add(int productId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Kiểm tra xem sản phẩm đã có trong wishlist chưa
            var exists = await _context.WishLists
                .AnyAsync(w => w.UserId == userId && w.ProductId == productId);

            if (exists)
            {
                return Json(new { success = false, message = "Sản phẩm đã có trong danh sách yêu thích!" });
            }

            var wishListItem = new WishList
            {
                UserId = userId,
                ProductId = productId,
                CreatedDate = DateTime.Now
            };

            _context.WishLists.Add(wishListItem);
            await _context.SaveChangesAsync();

            // Đếm số lượng wishlist
            var wishListCount = await _context.WishLists.CountAsync(w => w.UserId == userId);

            return Json(new { success = true, message = "Đã thêm vào danh sách yêu thích!", wishListCount });
        }

        // POST: WishList/Remove
        [HttpPost]
        public async Task<IActionResult> Remove(int productId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var wishListItem = await _context.WishLists
                .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId);

            if (wishListItem == null)
            {
                return Json(new { success = false, message = "Sản phẩm không có trong danh sách yêu thích!" });
            }

            _context.WishLists.Remove(wishListItem);
            await _context.SaveChangesAsync();

            // Đếm số lượng wishlist
            var wishListCount = await _context.WishLists.CountAsync(w => w.UserId == userId);

            return Json(new { success = true, message = "Đã xóa khỏi danh sách yêu thích!", wishListCount });
        }

        // GET: WishList/Toggle - Để check trạng thái và toggle
        [HttpPost]
        public async Task<IActionResult> Toggle(int productId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var wishListItem = await _context.WishLists
                .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId);

            bool isInWishList;
            string message;

            if (wishListItem != null)
            {
                // Đã có -> Xóa
                _context.WishLists.Remove(wishListItem);
                isInWishList = false;
                message = "Đã xóa khỏi danh sách yêu thích!";
            }
            else
            {
                // Chưa có -> Thêm
                _context.WishLists.Add(new WishList
                {
                    UserId = userId,
                    ProductId = productId,
                    CreatedDate = DateTime.Now
                });
                isInWishList = true;
                message = "Đã thêm vào danh sách yêu thích!";
            }

            await _context.SaveChangesAsync();

            // Đếm số lượng wishlist
            var wishListCount = await _context.WishLists.CountAsync(w => w.UserId == userId);

            return Json(new { success = true, message, isInWishList, wishListCount });
        }

        // GET: WishList/GetWishListStatus - Để load trạng thái các sản phẩm
        [HttpGet]
        public async Task<IActionResult> GetWishListStatus(int[] productIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var wishListProductIds = await _context.WishLists
                .Where(w => w.UserId == userId && productIds.Contains(w.ProductId))
                .Select(w => w.ProductId)
                .ToListAsync();

            return Json(wishListProductIds);
        }
    }
}
