using Bloomie.Data;
using Bloomie.Extensions;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bloomie.ViewComponents
{
    public class CartCountViewComponent : ViewComponent
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public CartCountViewComponent(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public IViewComponentResult Invoke()
        {
            var cartCount = GetCartCount();
            return Content(cartCount.ToString());
        }

        private int GetCartCount()
        {
            var cartKey = GetCartKey();
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey);
            // Exclude gift items from cart count
            return cart?.CartItems?.Where(i => !i.IsGift).Sum(i => i.Quantity) ?? 0;
        }

        private string GetCartKey()
        {
            var isAuthenticated = UserClaimsPrincipal?.Identity?.IsAuthenticated ?? false;
            string cartKey;

            if (isAuthenticated && UserClaimsPrincipal != null)
            {
                cartKey = $"Cart_{_userManager.GetUserId(UserClaimsPrincipal)}";
            }
            else
            {
                if (string.IsNullOrEmpty(HttpContext.Session.Id))
                {
                    HttpContext.Session.SetString("TempSessionId", Guid.NewGuid().ToString());
                }
                cartKey = $"Cart_Anonymous_{HttpContext.Session.GetString("TempSessionId") ?? HttpContext.Session.Id}";
            }

            return cartKey;
        }
    }
}
