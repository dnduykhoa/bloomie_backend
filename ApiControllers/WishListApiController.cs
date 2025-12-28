using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System.Security.Claims;

namespace Bloomie.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] 
    public class WishListApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public WishListApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Thêm sản phẩm vào wishlist
        /// </summary>
        [HttpPost("add")]
        public async Task<IActionResult> AddToWishList([FromBody] WishListRequest request)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });

                // Kiểm tra sản phẩm tồn tại
                var product = await _context.Products.FindAsync(request.ProductId);
                if (product == null || !product.IsActive)
                    return NotFound(new { success = false, message = "Sản phẩm không tồn tại" });

                // Kiểm tra đã có trong wishlist chưa
                var existing = await _context.WishLists
                    .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == request.ProductId);

                if (existing != null)
                    return BadRequest(new { success = false, message = "Sản phẩm đã có trong danh sách yêu thích" });

                // Thêm vào wishlist
                var wishListItem = new WishList
                {
                    UserId = userId,
                    ProductId = request.ProductId,
                    CreatedDate = DateTime.Now
                };

                _context.WishLists.Add(wishListItem);
                await _context.SaveChangesAsync();

                // Đếm số lượng trong wishlist
                var wishlistCount = await _context.WishLists.CountAsync(w => w.UserId == userId);

                return Ok(new
                {
                    success = true,
                    message = "Đã thêm vào danh sách yêu thích",
                    data = new
                    {
                        productId = request.ProductId,
                        wishlistCount = wishlistCount
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi thêm vào wishlist", error = ex.Message });
            }
        }

        /// <summary>
        /// Xóa sản phẩm khỏi wishlist
        /// </summary>
        [HttpDelete("remove/{productId}")]
        public async Task<IActionResult> RemoveFromWishList(int productId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });

                var wishListItem = await _context.WishLists
                    .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId);

                if (wishListItem == null)
                    return NotFound(new { success = false, message = "Sản phẩm không có trong danh sách yêu thích" });

                _context.WishLists.Remove(wishListItem);
                await _context.SaveChangesAsync();

                // Đếm số lượng trong wishlist
                var wishlistCount = await _context.WishLists.CountAsync(w => w.UserId == userId);

                return Ok(new
                {
                    success = true,
                    message = "Đã xóa khỏi danh sách yêu thích",
                    data = new
                    {
                        productId = productId,
                        wishlistCount = wishlistCount
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi xóa khỏi wishlist", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách wishlist của user
        /// </summary>
        [HttpGet("my-wishlist")]
        public async Task<IActionResult> GetMyWishList()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });

                var wishlistItems = await _context.WishLists
                    .Include(w => w.Product)
                        .ThenInclude(p => p!.Images)
                    .Where(w => w.UserId == userId && w.Product != null && w.Product.IsActive)
                    .OrderByDescending(w => w.CreatedDate)
                    .Select(w => new
                    {
                        w.Id,
                        w.ProductId,
                        CreatedDate = w.CreatedDate,
                        Product = new
                        {
                            w.Product!.Id,
                            w.Product.Name,
                            w.Product.Price,
                            w.Product.ImageUrl,
                            w.Product.StockQuantity,
                            w.Product.IsActive
                        }
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = wishlistItems,
                    count = wishlistItems.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi lấy danh sách wishlist", error = ex.Message });
            }
        }

        /// <summary>
        /// Toggle wishlist (add nếu chưa có, remove nếu đã có)
        /// </summary>
        [HttpPost("toggle")]
        public async Task<IActionResult> ToggleWishList([FromBody] WishListRequest request)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });

                var existing = await _context.WishLists
                    .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == request.ProductId);

                bool isAdded;
                if (existing != null)
                {
                    // Đã có → xóa
                    _context.WishLists.Remove(existing);
                    isAdded = false;
                }
                else
                {
                    // Chưa có → thêm
                    var product = await _context.Products.FindAsync(request.ProductId);
                    if (product == null || !product.IsActive)
                        return NotFound(new { success = false, message = "Sản phẩm không tồn tại" });

                    _context.WishLists.Add(new WishList
                    {
                        UserId = userId,
                        ProductId = request.ProductId,
                        CreatedDate = DateTime.Now
                    });
                    isAdded = true;
                }

                await _context.SaveChangesAsync();

                var wishlistCount = await _context.WishLists.CountAsync(w => w.UserId == userId);

                return Ok(new
                {
                    success = true,
                    message = isAdded ? "Đã thêm vào yêu thích" : "Đã xóa khỏi yêu thích",
                    data = new
                    {
                        productId = request.ProductId,
                        isInWishlist = isAdded,
                        wishlistCount = wishlistCount
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi toggle wishlist", error = ex.Message });
            }
        }
    }

    public class WishListRequest
    {
        public int ProductId { get; set; }
    }
}
