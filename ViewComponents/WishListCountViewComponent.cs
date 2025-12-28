using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Bloomie.ViewComponents
{
    public class WishListCountViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public WishListCountViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            if (string.IsNullOrEmpty(userId))
            {
                return Content("0");
            }

            var count = await _context.WishLists.CountAsync(w => w.UserId == userId);
            return Content(count.ToString());
        }
    }
}
