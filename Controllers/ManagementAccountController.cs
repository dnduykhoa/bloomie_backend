using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using Bloomie.Data;

namespace Bloomie.Controllers
{
    [Route("Management")]
    public class ManagementAccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private static readonly string[] AllowedRoles = { "Admin", "Staff", "Manager", "Shipper" };

        public ManagementAccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        [Route("Login")]
        [AllowAnonymous]
        public IActionResult Login(string returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [Route("Login")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản không tồn tại.");
                return View(model);
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            if (!userRoles.Any(r => AllowedRoles.Contains(r)))
            {
                ModelState.AddModelError(string.Empty, "Tài khoản không có quyền truy cập khu vực quản trị.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, model.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                // Nếu user cần đổi mật khẩu lần đầu
                if (user.RequirePasswordChange)
                {
                    // Đăng xuất user vừa đăng nhập để tránh truy cập các trang khác
                    await _signInManager.SignOutAsync();
                    TempData["RequirePasswordChange"] = true;
                    return RedirectToAction("ChangePassword", new { userId = user.Id });
                }
                // Chuyển hướng dựa trên role đến area riêng
                if (userRoles.Contains("Admin"))
                {
                    return RedirectToAction("Index", "AdminDashboard", new { area = "Admin" });
                }
                else if (userRoles.Contains("Manager"))
                {
                    return RedirectToAction("Index", "ManagerDashboard", new { area = "Manager" });
                }
                else if (userRoles.Contains("Staff"))
                {
                    return RedirectToAction("Index", "StaffDashboard", new { area = "Staff" });
                }
                else if (userRoles.Contains("Shipper"))
                {
                    return RedirectToAction("Index", "ShipperOrder", new { area = "Shipper" });
                }
                // Fallback to Admin
                return RedirectToAction("Index", "AdminDashboard", new { area = "Admin" });
            }
            else if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản bị khóa.");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Đăng nhập không thành công.");
            }
            return View(model);
        }

        [HttpGet]
        [Route("ChangePassword")]
        [AllowAnonymous]
        public async Task<IActionResult> ChangePassword(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }
            var model = new ChangePasswordViewModel { UserId = userId };
            return View(model);
        }

        [HttpPost]
        [Route("ChangePassword")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
            {
                return NotFound();
            }

            var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
            if (result.Succeeded)
            {
                user.RequirePasswordChange = false;
                await _userManager.UpdateAsync(user);
                TempData["success"] = "Đổi mật khẩu thành công. Bạn có thể đăng nhập lại.";
                return RedirectToAction("Login");
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        [HttpGet]
        [Route("Logout")]
        [Authorize(Roles = "Admin,Staff,Manager")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }
    }
}
