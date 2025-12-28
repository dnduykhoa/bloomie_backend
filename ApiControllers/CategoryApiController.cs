using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.ApiControllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoryApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CategoryApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/CategoryApi
        // Lấy tất cả categories cha với categories con (cấu trúc tree)
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                var categories = await _context.Categories
                    .Include(c => c.Children)
                    .Where(c => c.ParentId == null) // Chỉ lấy categories cha
                    .OrderBy(c => c.Type)
                    .ThenBy(c => c.Name)
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Type,
                        TypeName = c.Type == 0 ? "Chủ đề" : c.Type == 1 ? "Đối tượng" : c.Type == 2 ? "Kiểu dáng" : "Khác",
                        c.Description,
                        Children = c.Children!.OrderBy(ch => ch.Name).Select(ch => new
                        {
                            ch.Id,
                            ch.Name,
                            ch.Description,
                            ProductCount = ch.ProductCategories!.Count
                        }).ToList(),
                        ProductCount = c.ProductCategories!.Count
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = categories
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: api/CategoryApi/{id}
        // Lấy thông tin chi tiết 1 category
        [HttpGet("{id}")]
        public async Task<IActionResult> GetCategoryById(int id)
        {
            try
            {
                var category = await _context.Categories
                    .Include(c => c.Children)
                    .Include(c => c.Parent)
                    .Where(c => c.Id == id)
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        c.Type,
                        TypeName = c.Type == 0 ? "Chủ đề" : c.Type == 1 ? "Đối tượng" : c.Type == 2 ? "Kiểu dáng" : "Khác",
                        c.Description,
                        c.ParentId,
                        Parent = c.Parent != null ? new
                        {
                            c.Parent.Id,
                            c.Parent.Name
                        } : null,
                        Children = c.Children!.OrderBy(ch => ch.Name).Select(ch => new
                        {
                            ch.Id,
                            ch.Name,
                            ch.Description,
                            ProductCount = ch.ProductCategories!.Count
                        }).ToList(),
                        ProductCount = c.ProductCategories!.Count
                    })
                    .FirstOrDefaultAsync();

                if (category == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy danh mục"
                    });
                }

                return Ok(new
                {
                    success = true,
                    data = category
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: api/CategoryApi/{id}/products
        // Lấy tất cả sản phẩm thuộc 1 category (bao gồm cả categories con)
        [HttpGet("{id}/products")]
        public async Task<IActionResult> GetProductsByCategory(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
        {
            try
            {
                var category = await _context.Categories
                    .Include(c => c.Children)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (category == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Không tìm thấy danh mục"
                    });
                }

                // Lấy tất cả categoryIds (bao gồm cả categories con)
                var categoryIds = new List<int> { id };
                if (category.Children != null && category.Children.Any())
                {
                    categoryIds.AddRange(category.Children.Select(ch => ch.Id));
                }

                // Lấy sản phẩm thuộc các categoryIds
                var query = _context.Products
                    .Include(p => p.Images)
                    .Include(p => p.ProductCategories)
                    .ThenInclude(pc => pc.Category)
                    .Where(p => p.ProductCategories!.Any(pc => categoryIds.Contains(pc.CategoryId)))
                    .Where(p => p.IsActive);

                var totalProducts = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalProducts / (double)pageSize);

                var products = await query
                    .OrderBy(p => p.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.Id,
                        p.Name,
                        p.Price,
                        p.Description,
                        p.ImageUrl,
                        p.StockQuantity,
                        Images = p.Images!.Select(img => new { img.Id, img.Url }).ToList(),
                        Categories = p.ProductCategories!.Select(pc => new
                        {
                            pc.Category!.Id,
                            pc.Category.Name
                        }).ToList()
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = products,
                    pagination = new
                    {
                        currentPage = page,
                        pageSize = pageSize,
                        totalPages = totalPages,
                        totalItems = totalProducts
                    },
                    category = new
                    {
                        category.Id,
                        category.Name,
                        category.Type,
                        TypeName = category.Type == 0 ? "Chủ đề" : category.Type == 1 ? "Đối tượng" : category.Type == 2 ? "Kiểu dáng" : "Khác",
                        category.Description
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: api/CategoryApi/tree
        // Lấy cấu trúc tree đầy đủ của tất cả categories
        [HttpGet("tree")]
        public async Task<IActionResult> GetCategoryTree()
        {
            try
            {
                var allCategories = await _context.Categories
                    .OrderBy(c => c.Type)
                    .ThenBy(c => c.Name)
                    .ToListAsync();

                var categoryTree = allCategories
                    .Where(c => c.ParentId == null)
                    .Select(c => BuildCategoryTree(c, allCategories))
                    .ToList();

                return Ok(new
                {
                    success = true,
                    data = categoryTree
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // Helper method để build tree
        private object BuildCategoryTree(Category category, List<Category> allCategories)
        {
            var children = allCategories
                .Where(c => c.ParentId == category.Id)
                .Select(c => BuildCategoryTree(c, allCategories))
                .ToList();

            return new
            {
                category.Id,
                category.Name,
                category.Type,
                TypeName = category.Type == 0 ? "Chủ đề" : category.Type == 1 ? "Đối tượng" : category.Type == 2 ? "Kiểu dáng" : "Khác",
                category.Description,
                category.ParentId,
                HasChildren = children.Any(),
                Children = children
            };
        }
    }
}
