using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;

namespace Bloomie.Controllers
{
    public class BlogController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BlogController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Blog
        public async Task<IActionResult> Index()
        {
            var blogs = await _context.Blogs
                .Where(b => b.IsPublished)
                .OrderByDescending(b => b.PublishDate)
                .ToListAsync();

            return View(blogs);
        }

        // GET: Blog/Details/5 hoặc Blog/Details/slug
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            Blog? blog = null;

            // Thử tìm theo ID trước
            if (int.TryParse(id, out int blogId))
            {
                blog = await _context.Blogs
                    .FirstOrDefaultAsync(b => b.Id == blogId && b.IsPublished);
            }

            // Nếu không tìm thấy theo ID, thử tìm theo slug
            if (blog == null)
            {
                blog = await _context.Blogs
                    .FirstOrDefaultAsync(b => b.Slug == id && b.IsPublished);
            }

            if (blog == null)
            {
                return NotFound();
            }

            // Tăng view count
            blog.ViewCount++;
            await _context.SaveChangesAsync();

            return View(blog);
        }
    }
}
