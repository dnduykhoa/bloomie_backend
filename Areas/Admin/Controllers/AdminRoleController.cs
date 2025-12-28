using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.ViewModels;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminRoleController : Controller
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public AdminRoleController(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _context = context;
        }

        // GET: Admin/AdminRole
        public async Task<IActionResult> Index(string? searchString, string? statusFilter, int? minUsers, int? maxUsers)
        {
            // Calculate statistics
            var totalRoles = await _roleManager.Roles.CountAsync();
            var totalUsers = await _userManager.Users.CountAsync();
            
            var allRoles = await _roleManager.Roles.ToListAsync();
            int rolesWithUsers = 0;
            
            // Create dictionary to store user counts for each role
            var roleUserCounts = new Dictionary<string, int>();
            
            foreach (var role in allRoles)
            {
                if (!string.IsNullOrEmpty(role.Name))
                {
                    var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name);
                    var userCount = usersInRole.Count;
                    roleUserCounts[role.Id] = userCount;
                    
                    if (userCount > 0)
                    {
                        rolesWithUsers++;
                    }
                }
            }
            
            var emptyRoles = totalRoles - rolesWithUsers;

            ViewBag.TotalRoles = totalRoles;
            ViewBag.TotalUsers = totalUsers;
            ViewBag.RolesWithUsers = rolesWithUsers;
            ViewBag.EmptyRoles = emptyRoles;
            
            // Apply filters
            var query = _roleManager.Roles.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                query = query.Where(r => r.Name != null && r.Name.Contains(searchString));
                ViewBag.SearchString = searchString;
            }

            var roles = await query.ToListAsync();
            
            // Filter by status (hasUsers or empty)
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                if (statusFilter == "hasUsers")
                {
                    roles = roles.Where(r => roleUserCounts.ContainsKey(r.Id) && roleUserCounts[r.Id] > 0).ToList();
                }
                else if (statusFilter == "empty")
                {
                    roles = roles.Where(r => !roleUserCounts.ContainsKey(r.Id) || roleUserCounts[r.Id] == 0).ToList();
                }
                ViewBag.StatusFilter = statusFilter;
            }
            
            // Filter by user count range
            if (minUsers.HasValue)
            {
                roles = roles.Where(r => roleUserCounts.ContainsKey(r.Id) && roleUserCounts[r.Id] >= minUsers.Value).ToList();
                ViewBag.MinUsers = minUsers.Value;
            }
            
            if (maxUsers.HasValue)
            {
                roles = roles.Where(r => roleUserCounts.ContainsKey(r.Id) && roleUserCounts[r.Id] <= maxUsers.Value).ToList();
                ViewBag.MaxUsers = maxUsers.Value;
            }

            return View(roles);
        }

        // GET: Admin/AdminRole/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admin/AdminRole/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Required] string name)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra role đã tồn tại chưa
                var roleExist = await _roleManager.RoleExistsAsync(name);
                if (roleExist)
                {
                    TempData["error"] = $"Vai trò '{name}' đã tồn tại!";
                    return View();
                }

                var result = await _roleManager.CreateAsync(new IdentityRole(name));
                if (result.Succeeded)
                {
                    TempData["success"] = $"Tạo vai trò '{name}' thành công!";
                    return RedirectToAction(nameof(Index));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
            return View();
        }

        // GET: Admin/AdminRole/Edit/5
        public async Task<IActionResult> Edit(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            return View(role);
        }

        // POST: Admin/AdminRole/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, [Required] string name)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                // Kiểm tra tên mới có trùng với role khác không
                var existingRole = await _roleManager.FindByNameAsync(name);
                if (existingRole != null && existingRole.Id != id)
                {
                    TempData["error"] = $"Vai trò '{name}' đã tồn tại!";
                    return View(role);
                }

                role.Name = name;
                var result = await _roleManager.UpdateAsync(role);
                
                if (result.Succeeded)
                {
                    TempData["success"] = $"Cập nhật vai trò '{name}' thành công!";
                    return RedirectToAction(nameof(Index));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
            return View(role);
        }

        // GET: Admin/AdminRole/Delete/5
        public async Task<IActionResult> Delete(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            // Kiểm tra xem có user nào đang sử dụng role này không
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name);
            ViewBag.UsersCount = usersInRole.Count;

            return View(role);
        }

        // POST: Admin/AdminRole/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            // Kiểm tra lại có user nào đang sử dụng role này không
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name);
            if (usersInRole.Any())
            {
                TempData["error"] = $"Không thể xóa vai trò '{role.Name}' vì còn {usersInRole.Count} người dùng đang sử dụng!";
                return RedirectToAction(nameof(Index));
            }

            var result = await _roleManager.DeleteAsync(role);
            if (result.Succeeded)
            {
                TempData["success"] = $"Xóa vai trò '{role.Name}' thành công!";
            }
            else
            {
                TempData["error"] = $"Lỗi khi xóa vai trò: {string.Join(", ", result.Errors.Select(e => e.Description))}";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Admin/AdminRole/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            // Lấy danh sách user thuộc role này
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name);
            ViewBag.Users = usersInRole;

            return View(role);
        }

        // GET: Admin/AdminRole/ManageUserRoles?userId=xxx
        public async Task<IActionResult> ManageUserRoles(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            ViewBag.UserName = user.UserName;
            ViewBag.Email = user.Email;
            ViewBag.UserId = userId;

            var model = new List<UserRoleViewModel>();

            foreach (var role in _roleManager.Roles.ToList())
            {
                var userRoleViewModel = new UserRoleViewModel
                {
                    RoleId = role.Id,
                    RoleName = role.Name,
                    IsSelected = await _userManager.IsInRoleAsync(user, role.Name)
                };
                model.Add(userRoleViewModel);
            }

            return View(model);
        }

        // POST: Admin/AdminRole/ManageUserRoles
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManageUserRoles(string userId, List<UserRoleViewModel> model)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            // Lấy danh sách role hiện tại của user
            var currentRoles = await _userManager.GetRolesAsync(user);
            
            // Kiểm tra xem user có role Shipper cũ không
            bool hadShipperRole = currentRoles.Contains("Shipper");

            // Xóa tất cả role hiện tại
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                TempData["error"] = "Lỗi khi xóa vai trò cũ!";
                return RedirectToAction("ManageUserRoles", new { userId });
            }

            // Thêm các role được chọn
            var selectedRoles = model.Where(x => x.IsSelected).Select(x => x.RoleName).ToList();
            if (selectedRoles.Any())
            {
                var addResult = await _userManager.AddToRolesAsync(user, selectedRoles);
                if (!addResult.Succeeded)
                {
                    TempData["error"] = "Lỗi khi thêm vai trò mới!";
                    return RedirectToAction("ManageUserRoles", new { userId });
                }
            }
            
            // 🚴 TỰ ĐỘNG TẠO/XÓA SHIPPER PROFILE
            bool hasShipperRole = selectedRoles.Contains("Shipper");
            
            if (hasShipperRole && !hadShipperRole)
            {
                // User vừa được gán role Shipper → Tạo ShipperProfile
                var existingProfile = await _context.ShipperProfiles
                    .FirstOrDefaultAsync(s => s.UserId == userId);
                
                if (existingProfile == null)
                {
                    var shipperProfile = new Bloomie.Models.Entities.ShipperProfile
                    {
                        UserId = userId,
                        IsWorking = true,
                        MaxActiveOrders = 2,
                        CurrentActiveOrders = 0,
                        LastAssignedAt = null,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    
                    _context.ShipperProfiles.Add(shipperProfile);
                    await _context.SaveChangesAsync();
                    
                    Console.WriteLine($"✅ Đã tạo ShipperProfile cho user: {user.UserName} (ID: {userId})");
                }
            }
            else if (!hasShipperRole && hadShipperRole)
            {
                // User bị gỡ role Shipper → Xóa ShipperProfile
                var existingProfile = await _context.ShipperProfiles
                    .FirstOrDefaultAsync(s => s.UserId == userId);
                
                if (existingProfile != null)
                {
                    // Kiểm tra xem shipper có đơn hàng đang active không
                    var activeOrders = await _context.Orders
                        .Where(o => o.ShipperId == userId 
                            && (o.ShipperStatus == "Đã phân công" || o.ShipperStatus == "Đã xác nhận")
                            && o.Status != "Hoàn thành" 
                            && o.Status != "Đã hủy")
                        .CountAsync();
                    
                    if (activeOrders > 0)
                    {
                        TempData["warning"] = $"⚠️ Đã cập nhật vai trò nhưng không thể xóa ShipperProfile vì user đang có {activeOrders} đơn hàng active. Vui lòng chuyển đơn cho shipper khác trước.";
                        return RedirectToAction("Index", "AdminUser");
                    }
                    
                    _context.ShipperProfiles.Remove(existingProfile);
                    await _context.SaveChangesAsync();
                    
                    Console.WriteLine($"✅ Đã xóa ShipperProfile cho user: {user.UserName} (ID: {userId})");
                }
            }

            TempData["success"] = $"Cập nhật vai trò cho '{user.UserName}' thành công!";
            return RedirectToAction("Index", "AdminUser");
        }
    }
}
