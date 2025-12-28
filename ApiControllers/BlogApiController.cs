using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;

namespace Bloomie.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BlogApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public BlogApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ===== CHAT SUPPORT ENDPOINTS =====
        
        [HttpGet("search")]
        public async Task<IActionResult> SearchBlogs([FromQuery] string q = "")
        {
            try
            {
                var query = _context.Blogs.Where(b => b.IsPublished);

                // Nếu có query thì lọc theo title
                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(b => b.Title.Contains(q));
                }

                var blogs = await query
                    .OrderByDescending(b => b.PublishDate)
                    .Take(10)
                    .Select(b => new
                    {
                        blogId = b.Id,
                        title = b.Title,
                        summary = b.Excerpt,
                        imageUrl = b.ImageUrl,
                        publishDate = b.PublishDate
                    })
                    .ToListAsync();

                return Ok(blogs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi tìm kiếm bài viết", error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetBlogById(int id)
        {
            try
            {
                var blog = await _context.Blogs
                    .Where(b => b.Id == id)
                    .Select(b => new
                    {
                        blogId = b.Id,
                        title = b.Title,
                        content = b.Content, 
                        summary = b.Excerpt,
                        imageUrl = b.ImageUrl,
                        publishDate = b.PublishDate,
                        author = b.Author,    
                        viewCount = b.ViewCount 
                    })
                    .FirstOrDefaultAsync();

                if (blog == null)
                {
                    return NotFound(new { message = "Không tìm thấy bài viết" });
                }

                return Ok(blog);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy thông tin bài viết", error = ex.Message });
            }
        }
    }
}
