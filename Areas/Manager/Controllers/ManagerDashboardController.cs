using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;

namespace Bloomie.Areas.Manager.Controllers
{
    [Area("Manager")]
    [Authorize(Roles = "Manager")]
    public class ManagerDashboardController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;

        public ManagerDashboardController(
            UserManager<ApplicationUser> userManager, 
            RoleManager<IdentityRole> roleManager, 
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
        }

        // Dashboard chính cho Manager
        public async Task<IActionResult> Index()
        {
            var today = DateTime.UtcNow.Date;
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var viewModel = new DashboardViewModel
            {
                TotalUsers = await _userManager.Users.CountAsync(u => !u.IsDeleted),
                ActiveUsers = await _userManager.Users.CountAsync(u => !u.IsDeleted && (u.LockoutEnd == null || u.LockoutEnd < DateTime.UtcNow)),
                
                NewUsersToday = await _userManager.Users.CountAsync(u => u.CreatedAt.Date == today),
                NewUsersThisWeek = await _userManager.Users.CountAsync(u => u.CreatedAt.Date >= weekStart),
                NewUsersThisMonth = await _userManager.Users.CountAsync(u => u.CreatedAt.Date >= monthStart),
                
                LoginsTodayCount = await _context.LoginHistories.CountAsync(l => l.LoginTime.Date == today),
                LoginsThisWeekCount = await _context.LoginHistories.CountAsync(l => l.LoginTime.Date >= weekStart),
                
                UsersByRole = await GetUsersByRoleStatistics(),
                RecentActivities = await GetRecentActivities()
            };

            return View(viewModel);
        }

        private async Task<Dictionary<string, int>> GetUsersByRoleStatistics()
        {
            var result = new Dictionary<string, int>();
            var roles = await _roleManager.Roles.ToListAsync();

            foreach (var role in roles)
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
                result[role.Name!] = usersInRole.Count(u => !u.IsDeleted);
            }

            return result;
        }

        private async Task<List<RecentActivity>> GetRecentActivities()
        {
            var recentLogins = await _context.LoginHistories
                .Where(l => l.LoginTime >= DateTime.UtcNow.AddHours(-24))
                .OrderByDescending(l => l.LoginTime)
                .Take(10)
                .ToListAsync();

            var result = new List<RecentActivity>();
            foreach (var login in recentLogins)
            {
                var user = await _userManager.FindByIdAsync(login.UserId);
                if (user != null)
                {
                    result.Add(new RecentActivity
                    {
                        UserName = user.UserName ?? "Unknown",
                        Action = login.IsNewDevice ? "Đăng nhập từ thiết bị mới" : "Đăng nhập",
                        Timestamp = login.LoginTime,
                        IpAddress = login.IPAddress ?? "Unknown"
                    });
                }
            }

            return result;
        }
    }
}
