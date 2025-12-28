using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Bloomie.Models.ViewModels;
using Bloomie.Authorization;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminUserController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;

        public AdminUserController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            IEmailService emailService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _emailService = emailService;
        }

        // Thay thế method Index hiện tại
        public async Task<IActionResult> Index(string? searchString, string? roleFilter, string? statusFilter, DateTime? fromDate, DateTime? toDate)
        {
            // Calculate statistics
            var allUsers = await _userManager.Users.Where(u => !u.IsDeleted).ToListAsync();
            var totalUsers = allUsers.Count;
            var activeUsers = allUsers.Count(u => u.LockoutEnd == null || u.LockoutEnd < DateTime.UtcNow);
            var lockedUsers = allUsers.Count(u => u.LockoutEnd != null && u.LockoutEnd > DateTime.UtcNow);
            var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var newUsersThisMonth = allUsers.Count(u => u.CreatedAt >= startOfMonth);

            ViewBag.TotalUsers = totalUsers;
            ViewBag.ActiveUsers = activeUsers;
            ViewBag.LockedUsers = lockedUsers;
            ViewBag.NewUsersThisMonth = newUsersThisMonth;

            // Apply filters
            var query = _userManager.Users.Where(u => !u.IsDeleted).AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                query = query.Where(u => 
                    (u.Email != null && u.Email.Contains(searchString)) || 
                    (u.FullName != null && u.FullName.Contains(searchString))
                );
                ViewBag.SearchString = searchString;
            }

            var users = await query.ToListAsync();

            // Filter by role
            if (!string.IsNullOrWhiteSpace(roleFilter))
            {
                var usersInRole = new List<ApplicationUser>();
                foreach (var user in users)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Contains(roleFilter))
                    {
                        usersInRole.Add(user);
                    }
                }
                users = usersInRole;
                ViewBag.RoleFilter = roleFilter;
            }

            // Filter by status
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                if (statusFilter == "active")
                {
                    users = users.Where(u => u.LockoutEnd == null || u.LockoutEnd < DateTime.UtcNow).ToList();
                }
                else if (statusFilter == "locked")
                {
                    users = users.Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTime.UtcNow).ToList();
                }
                ViewBag.StatusFilter = statusFilter;
            }

            // Filter by date range
            if (fromDate.HasValue)
            {
                users = users.Where(u => u.CreatedAt.Date >= fromDate.Value.Date).ToList();
                ViewBag.FromDate = fromDate.Value.ToString("yyyy-MM-dd");
            }

            if (toDate.HasValue)
            {
                users = users.Where(u => u.CreatedAt.Date <= toDate.Value.Date).ToList();
                ViewBag.ToDate = toDate.Value.ToString("yyyy-MM-dd");
            }

            return View(users);
        }

        // Full page for deleted users
        public async Task<IActionResult> DeletedUsers(string? searchString, DateTime? fromDate, DateTime? toDate)
        {
            var query = _userManager.Users.Where(u => u.IsDeleted).AsQueryable();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                query = query.Where(u => 
                    (u.Email != null && u.Email.Contains(searchString)) || 
                    (u.FullName != null && u.FullName.Contains(searchString))
                );
                ViewBag.SearchString = searchString;
            }

            // Apply date range filter
            if (fromDate.HasValue && fromDate.Value != default)
            {
                query = query.Where(u => u.DeletedAt.HasValue && u.DeletedAt.Value.Date >= fromDate.Value.Date);
                ViewBag.FromDate = fromDate.Value.ToString("yyyy-MM-dd");
            }

            if (toDate.HasValue && toDate.Value != default)
            {
                query = query.Where(u => u.DeletedAt.HasValue && u.DeletedAt.Value.Date <= toDate.Value.Date);
                ViewBag.ToDate = toDate.Value.ToString("yyyy-MM-dd");
            }

            var users = await query.OrderByDescending(u => u.DeletedAt).ToListAsync();
            return View(users);
        }

        // Full page for recent activity
        public async Task<IActionResult> RecentActivity()
        {
            // Get recent activities from all users
            var recentLogins = await _context.LoginHistories
                .OrderByDescending(h => h.LoginTime)
                .Take(20)
                .ToListAsync();

            var recentAccess = await _context.UserAccessLogs
                .OrderByDescending(a => a.AccessTime)
                .Take(20)
                .ToListAsync();

            // Combine and sort
            var activities = new List<UserActivityViewModel>();

            foreach (var login in recentLogins)
            {
                var user = await _userManager.FindByIdAsync(login.UserId);
                activities.Add(new UserActivityViewModel
                {
                    Type = "Đăng nhập",
                    Description = login.IsNewDevice ? $"{user?.FullName ?? login.UserId} - Đăng nhập từ thiết bị mới" 
                                                     : $"{user?.FullName ?? login.UserId} - Đăng nhập",
                    Timestamp = login.LoginTime,
                    IpAddress = login.IPAddress,
                    DeviceInfo = login.UserAgent,
                    Status = login.IsNewDevice ? "warning" : "success"
                });
            }

            activities = activities.OrderByDescending(a => a.Timestamp).Take(50).ToList();

            return View(activities);
        }

        // GET: Hiển thị form thêm
        public async Task<IActionResult> Add()
        {
            // Lấy tất cả role từ database
            var roles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            ViewBag.Roles = roles;
            return View();
        }

        // POST: Xử lý thêm mới
        [HttpPost]
        public async Task<IActionResult> Add(ApplicationUser model, string role)
        {
            ModelState.Remove("RoleId");
            ModelState.Remove("Token");

            // 🎯 Kiểm tra quyền tạo user với role cụ thể
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(currentUserId!);
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser!);
            var currentUserRole = currentUserRoles.FirstOrDefault() ?? "User";

            bool canPromote = PermissionMatrix.UserManagement.CanPromoteToRole(
                currentUserRole,
                role,
                currentUser?.IsSuperAdmin ?? false
            );

            if (!canPromote)
            {
                TempData["error"] = $"Bạn không có quyền tạo người dùng với vai trò {role}.";
                return RedirectToAction("Index");
            }

            // Kiểm tra email trùng lặp
            if (!string.IsNullOrEmpty(model.Email))
            {
                var existingUserByEmail = await _userManager.FindByEmailAsync(model.Email);
                if (existingUserByEmail != null)
                {
                    ModelState.AddModelError("Email", "Email này đã được sử dụng.");
                }
            }

            // Kiểm tra username trùng lặp
            if (!string.IsNullOrEmpty(model.UserName))
            {
                var existingUserByName = await _userManager.FindByNameAsync(model.UserName);
                if (existingUserByName != null)
                {
                    ModelState.AddModelError("UserName", "Tên đăng nhập này đã được sử dụng.");
                }
            }

            // Nếu có lỗi, trả về view với thông báo
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var tempPassword = GenerateSecurePassword();

            // Tạo user nếu tất cả hợp lệ
            var user = new ApplicationUser
            {
                UserName = model.UserName,
                Email = model.Email,
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                RoleId = "",
                Token = Guid.NewGuid().ToString(),
                RequirePasswordChange = true,
                CreatedByUserId = currentUserId, // Ghi lại ai tạo user này
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, tempPassword);
            if (result.Succeeded)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
                if (!string.IsNullOrEmpty(role))
                {
                    await _userManager.AddToRoleAsync(user, role);
                    
                    // Tự động tạo ShipperProfile nếu role là Shipper
                    if (role == "Shipper")
                    {
                        var shipperProfile = new ShipperProfile
                        {
                            UserId = user.Id,
                            IsWorking = true,
                            MaxActiveOrders = 2,
                            CurrentActiveOrders = 0,
                            CreatedAt = DateTime.Now
                        };
                        _context.ShipperProfiles.Add(shipperProfile);
                        await _context.SaveChangesAsync();
                    }
                }
                await SendTempPasswordEmail(user, tempPassword);
                TempData["success"] = $"Thêm người dùng {role} thành công.";
                return RedirectToAction("Index");
            }

            // Nếu tạo user thất bại
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }

            return View(model);
        }

        // GET: Hiển thị form cập nhật
        public async Task<IActionResult> Update(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            ViewBag.CurrentRole = userRoles.FirstOrDefault() ?? "";
            ViewBag.AllRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            return View(user);
        }

        // POST: Xử lý cập nhật
        [HttpPost]
        public async Task<IActionResult> Update(ApplicationUser model, string role)
        {
            if (!string.IsNullOrEmpty(model.Email))
            {
                var existingUserByEmail = await _userManager.FindByEmailAsync(model.Email);
                if (existingUserByEmail != null && existingUserByEmail.Id != model.Id)
                {
                    ModelState.AddModelError("Email", "Email này đã được sử dụng.");
                }
            }

            if (!string.IsNullOrEmpty(model.UserName))
            {
                var existingUserByName = await _userManager.FindByNameAsync(model.UserName);
                if (existingUserByName != null && existingUserByName.Id != model.Id)
                {
                    ModelState.AddModelError("UserName", "Tên đăng nhập này đã được sử dụng.");
                }
            }

            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
            {
                return NotFound();
            }

            user.FullName = model.FullName;
            user.Email = model.Email;
            user.UserName = model.UserName;
            user.PhoneNumber = model.PhoneNumber;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                if (!string.IsNullOrEmpty(role))
                {
                    var currentRoles = await _userManager.GetRolesAsync(user);
                    if (currentRoles.Any())
                    {
                        await _userManager.RemoveFromRolesAsync(user, currentRoles);
                    }
                    await _userManager.AddToRoleAsync(user, role);
                }
                TempData["success"] = "Cập nhật thành công.";
                return RedirectToAction("Index");
            }
            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);
            return View(model);
        }

        // Xem chi tiết người dùng
        public async Task<IActionResult> Details(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            var roles = await _userManager.GetRolesAsync(user);
            ViewBag.Roles = roles;
            ViewBag.EmailConfirmed = await _userManager.IsEmailConfirmedAsync(user);
            return View(user);
        }

        // Xóa người dùng (soft delete)
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            return View(user);
        }

        [HttpPost, ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(currentUserId!);
            var targetUser = await _userManager.FindByIdAsync(id);
            
            if (targetUser == null)
            {
                return NotFound();
            }

            // 🔒 Kiểm tra: Không thể tự xóa chính mình
            if (targetUser.Id == currentUserId)
            {
                TempData["error"] = "Bạn không thể xóa tài khoản của chính mình.";
                return RedirectToAction("Index");
            }

            // 🔒 Kiểm tra: KHÔNG THỂ xóa Super Admin
            if (targetUser.IsSuperAdmin)
            {
                TempData["error"] = "Không thể xóa Super Admin.";
                return RedirectToAction("Index");
            }

            // 🎯 Lấy role của current user và target user
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser!);
            var targetUserRoles = await _userManager.GetRolesAsync(targetUser);
            
            var currentUserRole = currentUserRoles.FirstOrDefault() ?? "User";
            var targetUserRole = targetUserRoles.FirstOrDefault() ?? "User";

            // ⭐ Kiểm tra quyền xóa theo PermissionMatrix
            bool canDelete = PermissionMatrix.UserManagement.CanDelete(
                currentUserRole,
                targetUserRole,
                currentUser?.IsSuperAdmin ?? false,
                targetUser.IsSuperAdmin
            );

            if (!canDelete)
            {
                TempData["error"] = $"Bạn không có quyền xóa {targetUserRole}.";
                return RedirectToAction("Index");
            }

            // 🛡️ Bảo vệ: Không xóa Admin cuối cùng (trừ Super Admin)
            if (targetUserRole == "Admin")
            {
                var allAdmins = await _userManager.GetUsersInRoleAsync("Admin");
                var activeAdmins = allAdmins.Where(u => !u.IsDeleted && u.Id != id).ToList();
                
                if (activeAdmins.Count == 0)
                {
                    TempData["error"] = "Không thể xóa Admin cuối cùng trong hệ thống.";
                    return RedirectToAction("Index");
                }
            }

            // 🗑️ Xóa ShipperProfile nếu user là Shipper
            if (targetUserRole == "Shipper")
            {
                var shipperProfile = await _context.ShipperProfiles
                    .FirstOrDefaultAsync(sp => sp.UserId == id);
                
                if (shipperProfile != null)
                {
                    _context.ShipperProfiles.Remove(shipperProfile);
                    await _context.SaveChangesAsync();
                }
            }

            // Thực hiện soft delete
            targetUser.IsDeleted = true;
            targetUser.DeletedAt = DateTime.UtcNow;
            targetUser.DeleteReason = $"Xóa bởi {currentUser?.FullName ?? currentUserRole}";
            targetUser.LastModifiedDate = DateTime.UtcNow;
            targetUser.LastModifiedByUserId = currentUserId;
            
            await _userManager.UpdateAsync(targetUser);

            if (!string.IsNullOrEmpty(targetUser.Email))
            {
                await _emailService.SendEmailAsync(targetUser.Email, "Tài khoản của bạn đã bị xóa",
                    $"Tài khoản {targetUser.UserName} đã bị vô hiệu hóa bởi {currentUserRole} vào {DateTime.UtcNow:dd/MM/yyyy HH:mm}. Vui lòng liên hệ hỗ trợ nếu bạn cần khôi phục.");
            }

            TempData["success"] = $"Đã vô hiệu hóa tài khoản {targetUser.FullName}.";
            return RedirectToAction("Index");
        }

        // Xóa vĩnh viễn người dùng (hard delete)
        public async Task<IActionResult> HardDelete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            return View(user);
        }

        [HttpPost, ActionName("HardDelete")]
        public async Task<IActionResult> HardDeleteConfirmed(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            try
            {
                // Xóa tất cả dữ liệu liên quan đến user theo thứ tự phụ thuộc
                
                // 1. Lấy tất cả RatingIds của user trước
                var ratingIds = _context.Ratings.Where(r => r.UserId == id).Select(r => r.Id).ToList();

                // 1a. Xóa TẤT CẢ Reports liên quan đến Ratings của user (PHẢI XÓA TRƯỚC)
                if (ratingIds.Any())
                {
                    var reportsOnUserRatings = _context.Reports.Where(r => ratingIds.Contains(r.RatingId));
                    _context.Reports.RemoveRange(reportsOnUserRatings);
                }

                // 2. Xóa TẤT CẢ Replies liên quan đến các Ratings của user (bao gồm replies của người khác)
                if (ratingIds.Any())
                {
                    var allRepliesToRatings = _context.Replies.Where(r => ratingIds.Contains(r.RatingId));
                    
                    // Xóa ReplyImages của các replies này
                    var allReplyIds = allRepliesToRatings.Select(r => r.Id).ToList();
                    if (allReplyIds.Any())
                    {
                        var allReplyImages = _context.ReplyImages.Where(ri => allReplyIds.Contains(ri.ReplyId));
                        _context.ReplyImages.RemoveRange(allReplyImages);
                    }
                    
                    _context.Replies.RemoveRange(allRepliesToRatings);
                }

                // 3. Xóa Replies của chính user này
                var userReplies = _context.Replies.Where(r => r.UserId == id);
                
                var userReplyIds = userReplies.Select(r => r.Id).ToList();
                if (userReplyIds.Any())
                {
                    var userReplyImages = _context.ReplyImages.Where(ri => userReplyIds.Contains(ri.ReplyId));
                    _context.ReplyImages.RemoveRange(userReplyImages);
                }
                _context.Replies.RemoveRange(userReplies);

                // 4. Xóa RatingImages của user
                if (ratingIds.Any())
                {
                    var ratingImages = _context.RatingImages.Where(ri => ratingIds.Contains(ri.RatingId));
                    _context.RatingImages.RemoveRange(ratingImages);
                }

                // 5. Xóa Ratings của user
                var ratings = _context.Ratings.Where(r => r.UserId == id);
                _context.Ratings.RemoveRange(ratings);

                // 6. Xóa UserLikes của user
                var userLikes = _context.UserLikes.Where(ul => ul.UserId == id);
                _context.UserLikes.RemoveRange(userLikes);

                // 6. Xóa các bảng phụ thuộc vào Orders
                var orderIds = _context.Orders.Where(o => o.UserId == id).Select(o => o.Id).ToList();
                if (orderIds.Any())
                {
                    var orderDetails = _context.OrderDetails.Where(od => orderIds.Contains(od.OrderId));
                    _context.OrderDetails.RemoveRange(orderDetails);
                    
                    var orderReturns = _context.OrderReturns.Where(or => orderIds.Contains(or.OrderId));
                    _context.OrderReturns.RemoveRange(orderReturns);
                    
                    var serviceReviews = _context.ServiceReviews.Where(sr => orderIds.Contains(sr.OrderId));
                    _context.ServiceReviews.RemoveRange(serviceReviews);
                    
                    var promotionOrders = _context.PromotionOrders.Where(po => orderIds.Contains(po.OrderId));
                    _context.PromotionOrders.RemoveRange(promotionOrders);
                }

                // 7. Xóa Orders của user
                var orders = _context.Orders.Where(o => o.UserId == id);
                _context.Orders.RemoveRange(orders);

                // 8. Xóa CartItems của user
                var cartItems = _context.CartItems.Where(c => c.UserId == id);
                _context.CartItems.RemoveRange(cartItems);

                // 9. Xóa UserVouchers của user
                var userVouchers = _context.UserVouchers.Where(uv => uv.UserId == id);
                _context.UserVouchers.RemoveRange(userVouchers);

                // 10. Xóa LoginHistory của user
                var loginHistory = _context.LoginHistories.Where(lh => lh.UserId == id);
                _context.LoginHistories.RemoveRange(loginHistory);

                // 11. Xóa UserAccessLogs của user
                var accessLogs = _context.UserAccessLogs.Where(ual => ual.UserId == id);
                _context.UserAccessLogs.RemoveRange(accessLogs);

                // 12. Xóa UnlockRequests của user
                var unlockRequests = _context.UnlockRequests.Where(ur => ur.UserId == id);
                _context.UnlockRequests.RemoveRange(unlockRequests);

                // 13. Xóa WishLists của user
                var wishLists = _context.WishLists.Where(wl => wl.UserId == id);
                _context.WishLists.RemoveRange(wishLists);

                // 14. Xóa ShoppingCarts của user
                var shoppingCarts = _context.ShoppingCarts.Where(sc => sc.UserId == id);
                _context.ShoppingCarts.RemoveRange(shoppingCarts);

                // 15. Xóa UserCheckIns của user
                var userCheckIns = _context.UserCheckIns.Where(uc => uc.UserId == id);
                _context.UserCheckIns.RemoveRange(userCheckIns);

                // 16. Xóa PointRedemptions của user (phải xóa trước UserPoints)
                var pointRedemptions = _context.PointRedemptions.Where(pr => pr.UserId == id);
                _context.PointRedemptions.RemoveRange(pointRedemptions);

                // 17. Xóa PointHistories của user
                var pointHistories = _context.PointHistories.Where(ph => ph.UserId == id);
                _context.PointHistories.RemoveRange(pointHistories);

                // 18. Xóa UserPoints của user
                var userPoints = _context.UserPoints.Where(up => up.UserId == id);
                _context.UserPoints.RemoveRange(userPoints);

                // 19. Xóa Reports của user là reporter (Reports on user's ratings đã xóa ở bước 1a)
                var reportsAsReporter = _context.Reports.Where(r => r.ReporterId == id);
                _context.Reports.RemoveRange(reportsAsReporter);

                // 20. Xóa SupportMessages và SupportConversations của user
                // Lấy tất cả conversation IDs của user (cả Customer và Staff)
                var conversationIds = _context.SupportConversations
                    .Where(c => c.CustomerId == id || c.StaffId == id)
                    .Select(c => c.Id)
                    .ToList();

                if (conversationIds.Any())
                {
                    // Xóa tất cả messages trong các conversations này
                    var supportMessages = _context.SupportMessages
                        .Where(m => conversationIds.Contains(m.ConversationId));
                    _context.SupportMessages.RemoveRange(supportMessages);

                    // Xóa các conversations
                    var supportConversations = _context.SupportConversations
                        .Where(c => conversationIds.Contains(c.Id));
                    _context.SupportConversations.RemoveRange(supportConversations);
                }

                // 21. Xóa ChatMessages và ChatConversations của user (chatbot)
                try
                {
                    // ChatMessages chỉ có UserId, không có ChatConversationId
                    // Xóa messages của user trực tiếp
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM ChatMessages WHERE UserId = {0}", id);
                }
                catch (Exception)
                {
                    // ChatMessages table might not exist
                }

                try
                {
                    // Xóa ChatConversations của user
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM ChatConversations WHERE UserId = {0}", id);
                }
                catch (Exception)
                {
                    // ChatConversations table might not exist
                }

                // 22. Xóa Notifications của user
                var notifications = _context.Notifications.Where(n => n.UserId == id);
                _context.Notifications.RemoveRange(notifications);

                // 23. Xóa ShipperProfile nếu user là Shipper
                var shipperProfile = await _context.ShipperProfiles.FirstOrDefaultAsync(sp => sp.UserId == id);
                if (shipperProfile != null)
                {
                    _context.ShipperProfiles.Remove(shipperProfile);
                }

                // 24. Set NULL cho các user được tạo hoặc sửa bởi user này (ApplicationUser self-reference)
                var usersCreatedByThisUser = await _context.Users.Where(u => u.CreatedByUserId == id).ToListAsync();
                foreach (var u in usersCreatedByThisUser)
                {
                    u.CreatedByUserId = null;
                }

                var usersModifiedByThisUser = await _context.Users.Where(u => u.LastModifiedByUserId == id).ToListAsync();
                foreach (var u in usersModifiedByThisUser)
                {
                    u.LastModifiedByUserId = null;
                }

                await _context.SaveChangesAsync();

                // 25. Xóa các bảng Identity liên quan (AspNetUserClaims, AspNetUserLogins, AspNetUserTokens)
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUserClaims WHERE UserId = {0}", id);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUserLogins WHERE UserId = {0}", id);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUserTokens WHERE UserId = {0}", id);

                // 27. Xóa tất cả roles của user (AspNetUserRoles)
                var userRoles = await _userManager.GetRolesAsync(user);
                if (userRoles.Any())
                {
                    await _userManager.RemoveFromRolesAsync(user, userRoles);
                }

                // 28. Cuối cùng xóa user từ AspNetUsers
                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    TempData["success"] = "Đã xóa vĩnh viễn người dùng và toàn bộ dữ liệu liên quan.";
                }
                else
                {
                    // Nếu UserManager fail, thử xóa trực tiếp bằng SQL
                    try
                    {
                        await _context.Database.ExecuteSqlRawAsync("DELETE FROM AspNetUsers WHERE Id = {0}", id);
                        TempData["success"] = "Đã xóa vĩnh viễn người dùng và toàn bộ dữ liệu liên quan.";
                    }
                    catch (Exception sqlEx)
                    {
                        TempData["error"] = "Lỗi: " + string.Join(", ", result.Errors.Select(e => e.Description)) + " | SQL Error: " + sqlEx.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                TempData["error"] = $"Lỗi khi xóa người dùng: {innerMessage}";
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string[] userIds, string confirmPassword)
        {
            if (userIds == null || userIds.Length == 0)
            {
                TempData["error"] = "Vui lòng chọn ít nhất một người dùng.";
                return RedirectToAction("Index");
            }

            if (userIds.Length > 5)
            {
                TempData["error"] = "Chỉ được xóa tối đa 5 tài khoản cùng lúc";
                return RedirectToAction("Index");
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentAdmin = await _userManager.FindByIdAsync(currentUserId);

            if (string.IsNullOrEmpty(confirmPassword) || !await _userManager.CheckPasswordAsync(currentAdmin, confirmPassword))
            {
                TempData["error"] = "Mật khẩu xác nhận không đúng.";
                return RedirectToAction("Index");
            }

            int successCount = 0;
            int errorCount = 0;

            foreach (var userId in userIds)
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null && !await _userManager.IsInRoleAsync(user, "Admin") && userId != currentUserId)
                {
                    user.IsDeleted = true;
                    user.DeletedAt = DateTime.UtcNow;
                    var result = await _userManager.UpdateAsync(user);
                    if (result.Succeeded)
                    {
                        successCount++;
                        await _emailService.SendEmailAsync(user.Email, "Tài khoản của bạn đã được mở khóa",
                            $"Tài khoản {user.UserName} đã được mở khóa vào {DateTime.UtcNow:dd/MM/yyyy HH:mm}.");
                    }
                    else
                        errorCount++;
                }
                else
                {
                    errorCount++;
                }
            }

            TempData["success"] = $"Đã xóa {successCount} tài khoản.";
            if (errorCount > 0)
                TempData["error"] = $"Có {errorCount} tài khoản không thể xóa (Admin hoặc lỗi khác).";

            return RedirectToAction("Index");
        }

        // Khôi phục tài khoản đã xóa
        [HttpPost]
        public async Task<IActionResult> Restore(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            user.IsDeleted = false;
            user.DeletedAt = null;
            await _userManager.UpdateAsync(user);

            await _emailService.SendEmailAsync(user.Email, "Tài khoản của bạn đã được khôi phục",
                $"Tài khoản {user.UserName} đã được khôi phục bởi admin vào {DateTime.UtcNow:dd/MM/yyyy HH:mm}. Bạn có thể đăng nhập lại.");

            TempData["success"] = "Đã khôi phục tài khoản.";
            return RedirectToAction("DeletedUsers");
        }

        // Khóa tài khoản
        [HttpPost]
        public async Task<IActionResult> Lock(string id, int amount, string unit)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(currentUserId!);
            var targetUser = await _userManager.FindByIdAsync(id);
            
            if (targetUser == null)
            {
                return NotFound();
            }

            // 🔒 Không thể khóa chính mình
            if (targetUser.Id == currentUserId)
            {
                TempData["error"] = "Bạn không thể khóa tài khoản của chính mình.";
                return RedirectToAction("Details", new { id });
            }

            // 🔒 Không thể khóa Super Admin
            if (targetUser.IsSuperAdmin)
            {
                TempData["error"] = "Không thể khóa Super Admin.";
                return RedirectToAction("Details", new { id });
            }

            // 🎯 Kiểm tra quyền khóa
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser!);
            var targetUserRoles = await _userManager.GetRolesAsync(targetUser);
            
            var currentUserRole = currentUserRoles.FirstOrDefault() ?? "User";
            var targetUserRole = targetUserRoles.FirstOrDefault() ?? "User";

            bool canLock = PermissionMatrix.UserManagement.CanLockUnlock(
                currentUserRole,
                targetUserRole,
                currentUser?.IsSuperAdmin ?? false,
                targetUser.IsSuperAdmin
            );

            if (!canLock)
            {
                TempData["error"] = $"Bạn không có quyền khóa {targetUserRole}.";
                return RedirectToAction("Details", new { id });
            }

            if (amount < 1) amount = 1;
            DateTime lockoutEnd = DateTime.UtcNow;

            switch (unit)
            {
                case "minutes":
                    lockoutEnd = lockoutEnd.AddMinutes(amount);
                    break;
                case "hours":
                    lockoutEnd = lockoutEnd.AddHours(amount);
                    break;
                case "days":
                default:
                    lockoutEnd = lockoutEnd.AddDays(amount);
                    break;
            }

            // Bật lockout nếu chưa bật
            if (!targetUser.LockoutEnabled)
            {
                targetUser.LockoutEnabled = true;
                await _userManager.UpdateAsync(targetUser);
            }
    
            
            // Dùng API Identity để set lockout và invalidate session
            await _userManager.SetLockoutEndDateAsync(targetUser, lockoutEnd);
            await _userManager.UpdateSecurityStampAsync(targetUser); // làm mất hiệu lực cookie hiện tại
    
            TempData["success"] = $"Đã khóa tài khoản {targetUser.FullName} trong {amount} {unit}.";
            return RedirectToAction("Details", new { id });
        }

        // Khóa vĩnh viễn
        [HttpPost]
        public async Task<IActionResult> BanUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Khóa vĩnh viễn (đến năm 2099)
            user.LockoutEnd = DateTimeOffset.MaxValue;
            await _userManager.UpdateAsync(user);

            // Đăng xuất user khỏi tất cả thiết bị
            await _userManager.UpdateSecurityStampAsync(user);

            TempData["success"] = $"Đã khóa vĩnh viễn tài khoản {user.Email}.";
            return RedirectToAction("Index");
        }

        // Mở khóa tài khoản
        [HttpPost]
        public async Task<IActionResult> Unlock(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            user.LockoutEnd = null;
            await _userManager.UpdateAsync(user);
            TempData["success"] = "Đã mở khóa tài khoản.";
            return RedirectToAction("Index");
        }

        // Đặt lại mật khẩu
        [HttpPost]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Sinh mật khẩu tạm thời (12 ký tự ngẫu nhiên)
            var tempPassword = GenerateSecurePassword();

            // Đặt lại mật khẩu
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, tempPassword);

            if (result.Succeeded)
            {
                user.RequirePasswordChange = true;
                user.LockoutEnd = null;
                user.EmailConfirmed = true;
                user.Token = Guid.NewGuid().ToString();
                await _userManager.UpdateAsync(user);

                // Gửi email cho user
                await _emailService.SendEmailAsync(user.Email, "Mật khẩu tạm thời Bloomie",
                    $"Mật khẩu tạm thời của bạn là: <strong>{tempPassword}</strong>. Vui lòng đăng nhập và đổi lại mật khẩu ngay.");

                TempData["success"] = "Đã gửi mật khẩu tạm thời cho người dùng qua email.";
            }
            else
            {
                TempData["error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction("Details", new { id });
        }

        // Sinh mật khẩu ngẫu nhiên an toàn
        private string GenerateSecurePassword()
        {
            const int length = 12;
            const string validChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz0123456789!@#$%^&*()_+-=[]{}|;:,.<>?";

            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);

            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = validChars[bytes[i] % validChars.Length];
            }

            return new string(result);
        }

        private async Task SendTempPasswordEmail(ApplicationUser user, string tempPassword)
        {
            var subject = "Tài khoản Bloomie đã được tạo";
            var message = $@"
                <h3>Chào {user.FullName},</h3>
                <p>Tài khoản Bloomie của bạn đã được tạo thành công!</p>
                <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 15px 0;'>
                    <strong>Thông tin đăng nhập:</strong><br/>
                    👤 <strong>Username:</strong> {user.UserName}<br/>
                    🔑 <strong>Mật khẩu tạm thời:</strong> {tempPassword}
                </div>
                <div style='color: #dc3545; font-weight: bold;'>
                    ⚠️ BẮT BUỘC: Bạn phải đổi mật khẩu ngay khi đăng nhập lần đầu
                </div>";

            await _emailService.SendEmailAsync(user.Email, subject, message);
        }

        [HttpPost]
        public async Task<IActionResult> SetTwoFactor(string id, bool enable)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            await _userManager.SetTwoFactorEnabledAsync(user, enable);
            TempData["success"] = $"Đã {(enable ? "bật" : "tắt")} xác thực hai yếu tố cho người dùng.";
            return RedirectToAction("Details", new { id });
        }

        // Phân quyền (gán role)
        [HttpPost]
        public async Task<IActionResult> SetRole(string id, string role)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Lấy role hiện tại của user
            var currentRoles = await _userManager.GetRolesAsync(user);

            // Lấy user thực hiện thao tác
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(currentUserId);
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);

            // Không cho phép tự nâng quyền cho chính mình lên Admin
            if (user.Id == currentUserId && role == "Admin" && !currentUserRoles.Contains("Admin"))
            {
                TempData["error"] = "Bạn không thể tự nâng quyền cho chính mình lên Admin.";
                return RedirectToAction("Details", new { id });
            }

            // Không cho phép hạ quyền Admin cuối cùng
            if (currentRoles.Contains("Admin") && role != "Admin")
            {
                var adminCount = await _userManager.GetUsersInRoleAsync("Admin");
                if (adminCount.Count <= 1)
                {
                    TempData["error"] = "Không thể hạ quyền Admin cuối cùng.";
                    return RedirectToAction("Details", new { id });
                }
            }

            // Xóa các role cũ và gán role mới
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, role);

            TempData["success"] = "Đã cập nhật vai trò.";
            return RedirectToAction("Details", new { id });
        }

        // Xem lịch sử đăng nhập của user (Admin xem user khác)
        public async Task<IActionResult> LoginHistory(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Lấy lịch sử đăng nhập từ database
            var loginHistory = await _context.LoginHistories
                .Where(h => h.UserId == id)
                .OrderByDescending(h => h.LoginTime)
                .Take(50) // Lấy 50 lần đăng nhập gần nhất
                .ToListAsync();

            ViewBag.User = user;
            ViewBag.IsAdminView = true;
            return View(loginHistory);
        }

        // Xem lịch sử truy cập trang của user
        public async Task<IActionResult> AccessHistory(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Lấy lịch sử truy cập từ database
            var accessHistory = await _context.UserAccessLogs
                .Where(a => a.UserId == id)
                .OrderByDescending(a => a.AccessTime)
                .Take(100) // Lấy 100 lần truy cập gần nhất
                .ToListAsync();

            ViewBag.User = user;
            return View(accessHistory);
        }

        // Xem tổng hợp hoạt động của user
        public async Task<IActionResult> UserActivity(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Lấy dữ liệu từ cả 2 bảng
            var loginHistory = await _context.LoginHistories
                .Where(h => h.UserId == id)
                .OrderByDescending(h => h.LoginTime)
                .Take(20)
                .ToListAsync();

            var accessHistory = await _context.UserAccessLogs
                .Where(a => a.UserId == id)
                .OrderByDescending(a => a.AccessTime)
                .Take(20)
                .ToListAsync();

            // Tạo ViewModel tổng hợp
            var activities = new List<UserActivityViewModel>();

            // Thêm lịch sử đăng nhập
            foreach (var login in loginHistory)
            {
                activities.Add(new UserActivityViewModel
                {
                    Type = "Đăng nhập",
                    Description = login.IsNewDevice ? "Đăng nhập từ thiết bị mới" : "Đăng nhập",
                    Timestamp = login.LoginTime,
                    IpAddress = login.IPAddress,
                    DeviceInfo = login.UserAgent,
                    Status = login.IsNewDevice ? "warning" : "success"
                });
            }

            // Thêm lịch sử truy cập
            foreach (var access in accessHistory)
            {
                activities.Add(new UserActivityViewModel
                {
                    Type = "Truy cập trang",
                    Description = $"Truy cập {access.Url}",
                    Timestamp = access.AccessTime,
                    IpAddress = "N/A",
                    DeviceInfo = "Web Browser",
                    Status = "info"
                });
            }

            // Thêm thông tin từ ApplicationUser
            activities.Add(new UserActivityViewModel
            {
                Type = "Tạo tài khoản",
                Description = "Tài khoản được tạo trong hệ thống",
                Timestamp = user.CreatedAt,
                IpAddress = "System",
                DeviceInfo = "Admin Panel",
                Status = "success"
            });

            if (user.IsDeleted && user.DeletedAt.HasValue)
            {
                activities.Add(new UserActivityViewModel
                {
                    Type = "Xóa tài khoản",
                    Description = $"Tài khoản bị xóa: {user.DeleteReason}",
                    Timestamp = user.DeletedAt.Value,
                    IpAddress = "Admin",
                    DeviceInfo = "Admin Panel",
                    Status = "danger"
                });
            }

            // Sắp xếp theo thời gian
            activities = activities.OrderByDescending(a => a.Timestamp).Take(50).ToList();

            ViewBag.User = user;
            return View(activities);
        }

        // GET: Trang xuất dữ liệu
        public IActionResult Export()
        {
            var today = DateTime.UtcNow;
            var lastMonth = today.AddHours(7).AddMonths(-1);

            ViewBag.DefaultDateFrom = lastMonth.ToString("yyyy-MM-dd");
            ViewBag.DefaultDateTo = today.ToString("yyyy-MM-dd");

            return View();
        }

        // GET: Trang nhập dữ liệu  
        public IActionResult Import()
        {
            return View();
        }

        // POST: Xuất danh sách người dùng 
        [HttpPost]
        public async Task<IActionResult> ExportUsers(ExportUsersRequest request)
        {
            try
            {
                var query = _userManager.Users.AsQueryable();

                // Lọc theo trạng thái
                if (!request.IncludeDeleted)
                {
                    query = query.Where(u => !u.IsDeleted);
                }

                // Lọc theo ngày tạo
                if (request.DateFrom.HasValue)
                {
                    query = query.Where(u => u.CreatedAt >= request.DateFrom.Value);
                }

                if (request.DateTo.HasValue)
                {
                    query = query.Where(u => u.CreatedAt <= request.DateTo.Value.AddDays(1));
                }

                var users = await query.OrderBy(u => u.CreatedAt).ToListAsync();

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Danh sách người dùng");

                // Tiêu đề
                worksheet.Cell(1, 1).Value = "ID";
                worksheet.Cell(1, 2).Value = "Tên đăng nhập";
                worksheet.Cell(1, 3).Value = "Email";
                worksheet.Cell(1, 4).Value = "Họ tên";
                worksheet.Cell(1, 5).Value = "Số điện thoại";
                worksheet.Cell(1, 6).Value = "Ngày tạo";
                worksheet.Cell(1, 7).Value = "Trạng thái";

                if (request.IncludeRoles)
                {
                    worksheet.Cell(1, 8).Value = "Vai trò";
                }

                // Định dạng tiêu đề
                var headerRange = worksheet.Range(1, 1, 1, request.IncludeRoles ? 8 : 7);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

                // Data
                int row = 2;
                foreach (var user in users)
                {
                    worksheet.Cell(row, 1).Value = user.Id;
                    worksheet.Cell(row, 2).Value = user.UserName;
                    worksheet.Cell(row, 3).Value = user.Email;
                    worksheet.Cell(row, 4).Value = user.FullName;
                    worksheet.Cell(row, 5).Value = user.PhoneNumber ?? "";
                    worksheet.Cell(row, 6).Value = user.CreatedAt.ToString("dd/MM/yyyy HH:mm");

                    string status = user.IsDeleted ? "Đã xóa" :
                                   (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow) ? "Bị khóa" : "Hoạt động";
                    worksheet.Cell(row, 7).Value = status;

                    if (request.IncludeRoles)
                    {
                        var roles = await _userManager.GetRolesAsync(user);
                        worksheet.Cell(row, 8).Value = string.Join(", ", roles);
                    }

                    row++;
                }

                // Tự động điều chỉnh độ rộng cột
                worksheet.ColumnsUsed().AdjustToContents();

                var fileName = $"DanhSach_NguoiDung_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                var fileBytes = stream.ToArray();

                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                TempData["error"] = $"Lỗi xuất file: {ex.Message}";
                return RedirectToAction("Export");
            }
        }

        // POST: Xuất lịch sử đăng nhập 
        [HttpPost]
        public async Task<IActionResult> ExportLoginHistory(ExportLoginHistoryRequest request)
        {
            try
            {
                var query = _context.LoginHistories.AsQueryable();

                if (request.DateFrom.HasValue)
                {
                    query = query.Where(l => l.LoginTime >= request.DateFrom.Value);
                }

                if (request.DateTo.HasValue)
                {
                    query = query.Where(l => l.LoginTime <= request.DateTo.Value.AddDays(1));
                }

                if (!string.IsNullOrEmpty(request.UserId))
                {
                    query = query.Where(l => l.UserId == request.UserId);
                }

                var loginHistory = await query.OrderByDescending(l => l.LoginTime).ToListAsync();

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Lịch sử đăng nhập");

                // Tiêu đề
                worksheet.Cell(1, 1).Value = "Thời gian";
                worksheet.Cell(1, 2).Value = "Người dùng";
                worksheet.Cell(1, 3).Value = "Email";
                worksheet.Cell(1, 4).Value = "IP Address";
                worksheet.Cell(1, 5).Value = "Thiết bị";
                worksheet.Cell(1, 6).Value = "Thiết bị mới";

                // Định dạng tiêu đề
                var headerRange = worksheet.Range(1, 1, 1, 6);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

                // Data
                int row = 2;
                foreach (var login in loginHistory)
                {
                    worksheet.Cell(row, 1).Value = login.LoginTime.ToString("dd/MM/yyyy HH:mm:ss");
                    var user = await _userManager.FindByIdAsync(login.UserId);
                    worksheet.Cell(row, 2).Value = user?.UserName ?? "N/A";
                    worksheet.Cell(row, 3).Value = user?.Email ?? "N/A";
                    worksheet.Cell(row, 4).Value = login.IPAddress;
                    worksheet.Cell(row, 5).Value = login.UserAgent;
                    worksheet.Cell(row, 6).Value = login.IsNewDevice ? "Có" : "Không";
                    row++;
                }

                worksheet.ColumnsUsed().AdjustToContents();

                var fileName = $"LichSu_DangNhap_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                var fileBytes = stream.ToArray();

                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                TempData["error"] = $"Lỗi xuất file: {ex.Message}";
                return RedirectToAction("Export");
            }
        }

        // POST: Nhập người dùng từ file Excel
        [HttpPost]
        public async Task<IActionResult> ImportUsers(ImportUsersRequest request)
        {
            if (request.File == null || request.File.Length == 0)
            {
                TempData["error"] = "Vui lòng chọn file Excel để nhập.";
                return RedirectToAction("Import");
            }

            try
            {
                var result = await ProcessUserImport(request.File, request);

                if (result.SuccessCount > 0)
                {
                    TempData["success"] = $"Nhập thành công {result.SuccessCount}/{result.TotalRows} người dùng.";
                }

                if (result.ErrorCount > 0)
                {
                    TempData["warning"] = $"Có {result.ErrorCount} lỗi: {string.Join("; ", result.Errors.Take(3))}";
                }

                return RedirectToAction("Import");
            }
            catch (Exception ex)
            {
                TempData["error"] = $"Lỗi nhập file: {ex.Message}";
                return RedirectToAction("Import");
            }
        }


        // GET: File mẫu Excel cho người dùng
        public IActionResult DownloadUserTemplate()
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Danh sách người dùng");

            // ========== HEADER SECTION ==========
            // Main title
            worksheet.Cell(1, 1).Value = "BLOOMIE - MẪU NHẬP DANH SÁCH NGƯỜI DÙNG";
            worksheet.Range(1, 1, 1, 5).Merge();
            worksheet.Range(1, 1, 1, 5).Style
                .Font.SetBold(true)
                .Font.SetFontSize(14)
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#0d6efd"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            worksheet.Row(1).Height = 25;

            // Instructions
            worksheet.Cell(2, 1).Value = "Hướng dẫn: Điền thông tin từ dòng 10 trở xuống. KHÔNG xóa hoặc sửa dòng tiêu đề (dòng 9). Tên đăng nhập tự động sinh từ email.";
            worksheet.Range(2, 1, 2, 5).Merge();
            worksheet.Range(2, 1, 2, 5).Style
                .Font.SetItalic(true)
                .Font.SetFontColor(XLColor.FromHtml("#6c757d"))
                .Fill.SetBackgroundColor(XLColor.FromHtml("#fff3cd"))
                .Alignment.SetWrapText(true);
            worksheet.Row(2).Height = 30;

            // ========== VALIDATION RULES ==========
            worksheet.Cell(3, 1).Value = "CỘT";
            worksheet.Cell(3, 2).Value = "TÊN TRƯỜNG";
            worksheet.Cell(3, 3).Value = "BẮT BUỘC";
            worksheet.Cell(3, 4).Value = "QUY TẮC";
            worksheet.Cell(3, 5).Value = "VÍ DỤ";
            
            var validationHeaderRange = worksheet.Range(3, 1, 3, 5);
            validationHeaderRange.Style
                .Font.SetBold(true)
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#198754"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Row 4: Column A - Email (BẮT BUỘC)
            worksheet.Cell(4, 1).Value = "A";
            worksheet.Cell(4, 2).Value = "Email";
            worksheet.Cell(4, 3).Value = "✓ BẮT BUỘC";
            worksheet.Cell(4, 4).Value = "Email hợp lệ, không trùng lặp";
            worksheet.Cell(4, 5).Value = "nguyenvana@example.com";
            worksheet.Cell(4, 3).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#ffebee"));

            // Row 5: Column B - Full Name (BẮT BUỘC)
            worksheet.Cell(5, 1).Value = "B";
            worksheet.Cell(5, 2).Value = "Họ và tên";
            worksheet.Cell(5, 3).Value = "✓ BẮT BUỘC";
            worksheet.Cell(5, 4).Value = "Họ tên đầy đủ";
            worksheet.Cell(5, 5).Value = "Nguyễn Văn A";
            worksheet.Cell(5, 3).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#ffebee"));

            // Row 6: Column C - Phone (TÙY CHỌN)
            worksheet.Cell(6, 1).Value = "C";
            worksheet.Cell(6, 2).Value = "Số điện thoại";
            worksheet.Cell(6, 3).Value = "Tùy chọn";
            worksheet.Cell(6, 4).Value = "10-11 số, bắt đầu bằng 0";
            worksheet.Cell(6, 5).Value = "0123456789";
            worksheet.Cell(6, 3).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#e3f2fd"));

            // Row 7: Column D - Role (TÙY CHỌN)
            worksheet.Cell(7, 1).Value = "D";
            worksheet.Cell(7, 2).Value = "Vai trò";
            worksheet.Cell(7, 3).Value = "Tùy chọn";
            worksheet.Cell(7, 4).Value = "User / Staff / Manager (mặc định: User)";
            worksheet.Cell(7, 5).Value = "User";
            worksheet.Cell(7, 3).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#e3f2fd"));

            var validationRange = worksheet.Range(4, 1, 7, 5);
            validationRange.Style
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                .Alignment.SetWrapText(true);

            // ========== DATA SECTION ==========
            worksheet.Cell(9, 1).Value = "Email";
            worksheet.Cell(9, 2).Value = "Họ và tên";
            worksheet.Cell(9, 3).Value = "Số điện thoại";
            worksheet.Cell(9, 4).Value = "Vai trò";

            var dataHeaderRange = worksheet.Range(9, 1, 9, 4);
            dataHeaderRange.Style
                .Font.SetBold(true)
                .Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#0d6efd"))
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Border.SetOutsideBorder(XLBorderStyleValues.Medium);

            // ========== SAMPLE DATA ==========
            // Sample 1 - User
            worksheet.Cell(10, 1).Value = "nguyenvana@example.com";
            worksheet.Cell(10, 2).Value = "Nguyễn Văn A";
            worksheet.Cell(10, 3).Value = "0123456789";
            worksheet.Cell(10, 4).Value = "User";

            // Sample 2 - User
            worksheet.Cell(11, 1).Value = "tranthib@example.com";
            worksheet.Cell(11, 2).Value = "Trần Thị B";
            worksheet.Cell(11, 3).Value = "0987654321";
            worksheet.Cell(11, 4).Value = "User";

            // Sample 3 - Staff
            worksheet.Cell(12, 1).Value = "levantam@example.com";
            worksheet.Cell(12, 2).Value = "Lê Văn Tâm";
            worksheet.Cell(12, 3).Value = "0912345678";
            worksheet.Cell(12, 4).Value = "Staff";

            // Sample 4 - Manager
            worksheet.Cell(13, 1).Value = "phamthiyen@example.com";
            worksheet.Cell(13, 2).Value = "Phạm Thị Yến";
            worksheet.Cell(13, 3).Value = "0909123456";
            worksheet.Cell(13, 4).Value = "Manager";

            // Sample 5 - No phone, default role
            worksheet.Cell(14, 1).Value = "hoangvandung@example.com";
            worksheet.Cell(14, 2).Value = "Hoàng Văn Dũng";
            worksheet.Cell(14, 3).Value = "";
            worksheet.Cell(14, 4).Value = "";

            // Style sample data
            var sampleRange = worksheet.Range(10, 1, 14, 4);
            sampleRange.Style
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#f8f9fa"));

            // Alternate row colors for better readability
            for (int i = 10; i <= 14; i++)
            {
                if ((i - 10) % 2 == 0)
                {
                    worksheet.Range(i, 1, i, 4).Style.Fill.SetBackgroundColor(XLColor.White);
                }
            }

            // ========== FOOTER NOTES ==========
            worksheet.Cell(16, 1).Value = "📝 LƯU Ý QUAN TRỌNG:";
            worksheet.Cell(16, 1).Style.Font.SetBold(true).Font.SetFontSize(12);
            
            worksheet.Cell(17, 1).Value = "• Xóa các dòng mẫu (dòng 10-14) trước khi nhập dữ liệu thực";
            worksheet.Cell(18, 1).Value = "• Tên đăng nhập tự động tạo từ email (phần trước ký tự @)";
            worksheet.Cell(19, 1).Value = "• Mật khẩu tạm thời sẽ được tạo tự động và gửi qua email";
            worksheet.Cell(20, 1).Value = "• Email phải là duy nhất trong hệ thống";
            worksheet.Cell(21, 1).Value = "• Vai trò để trống sẽ mặc định là 'User'";
            worksheet.Cell(22, 1).Value = "• Sau khi import, người dùng sẽ được yêu cầu đổi mật khẩu lần đầu đăng nhập";

            var notesRange = worksheet.Range(17, 1, 22, 4);
            notesRange.Style.Font.SetItalic(true).Font.SetFontColor(XLColor.FromHtml("#dc3545"));
            // Set wrap text for each cell in the range
            for (int row = 17; row <= 22; row++)
            {
                for (int col = 1; col <= 4; col++)
                {
                    worksheet.Cell(row, col).Style.Alignment.SetWrapText(true);
                }
            }

            // ========== COLUMN FORMATTING ==========
            worksheet.Column(1).Width = 30; // Email
            worksheet.Column(2).Width = 25; // Full Name
            worksheet.Column(3).Width = 18; // Phone
            worksheet.Column(4).Width = 15; // Role

            // Add data validation for Role column (from row 10 onwards)
            var roleValidation = worksheet.Range(10, 4, 1000, 4).CreateDataValidation();
            roleValidation.List("User,Staff,Manager", true);
            roleValidation.ErrorTitle = "Vai trò không hợp lệ";
            roleValidation.ErrorMessage = "Vui lòng chọn: User, Staff hoặc Manager";
            roleValidation.ShowErrorMessage = true;

            // Freeze header row
            worksheet.SheetView.FreezeRows(9);

            var fileName = $"Bloomie_Mau_NguoiDung_{DateTime.Now:yyyyMMdd}.xlsx";

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileBytes = stream.ToArray();

            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // Phương thức hỗ trợ xử lý nhập dữ liệu
        private async Task<ImportResult> ProcessUserImport(IFormFile file, ImportUsersRequest request)
        {
            var result = new ImportResult();

            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);

            // Tìm dòng header (dòng 9 trong template mới)
            var allRows = worksheet.RangeUsed().RowsUsed().ToList();
            int headerRowNumber = 1; // Default
            
            // Auto-detect header row by looking for "Email" in column 1
            var detectedHeader = allRows.FirstOrDefault(r => 
            {
                var cell1 = r.Cell(1).GetString().Trim();
                // Look for header row with "Email" in column 1
                return cell1.Contains("Email", StringComparison.OrdinalIgnoreCase);
            });
            
            if (detectedHeader != null)
            {
                headerRowNumber = detectedHeader.RowNumber();
            }

            // Get data rows (skip header and everything before it)
            var dataRows = allRows.Where(r => r.RowNumber() > headerRowNumber).ToList();
            int processedCount = 0;

            foreach (var row in dataRows)
            {
                try
                {
                    var emailValue = row.Cell(1).GetString().Trim();
                    
                    // Skip empty rows
                    if (row.IsEmpty() || string.IsNullOrWhiteSpace(emailValue))
                    {
                        continue;
                    }

                    // Skip instruction/note rows (containing special characters or keywords)
                    if (emailValue.StartsWith("•") || 
                        emailValue.StartsWith("GHI CHÚ", StringComparison.OrdinalIgnoreCase) ||
                        emailValue.StartsWith("LƯU Ý", StringComparison.OrdinalIgnoreCase) ||
                        emailValue.Contains("mẫu", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Skip if email doesn't contain @
                    if (!emailValue.Contains("@"))
                    {
                        continue;
                    }

                    processedCount++;
                    
                    var userModel = new UserImportModel
                    {
                        Email = emailValue,
                        FullName = row.Cell(2).GetString().Trim(),
                        PhoneNumber = row.Cell(3).GetString().Trim(),
                        Role = row.Cell(4).GetString().Trim()
                    };

                    // Kiểm tra dữ liệu bắt buộc
                    if (string.IsNullOrEmpty(userModel.Email) || string.IsNullOrEmpty(userModel.FullName))
                    {
                        result.ErrorCount++;
                        result.Errors.Add($"Dòng {row.RowNumber()}: Thiếu email hoặc họ tên");
                        continue;
                    }

                    // Validate email format
                    if (!IsValidEmail(userModel.Email))
                    {
                        result.ErrorCount++;
                        result.Errors.Add($"Dòng {row.RowNumber()}: Email '{userModel.Email}' không hợp lệ");
                        continue;
                    }

                    // Auto-generate username from email
                    var baseUsername = GenerateUsernameFromEmail(userModel.Email);
                    userModel.UserName = await EnsureUniqueUsername(baseUsername);

                    // Kiểm tra người dùng đã tồn tại
                    var existingUser = await _userManager.FindByEmailAsync(userModel.Email);
                    if (existingUser != null)
                    {
                        result.ErrorCount++;
                        result.Errors.Add($"Dòng {row.RowNumber()}: Email {userModel.Email} đã tồn tại");
                        continue;
                    }

                    // Tạo người dùng mới
                    var user = new ApplicationUser
                    {
                        UserName = userModel.UserName,
                        Email = userModel.Email,
                        FullName = userModel.FullName,
                        PhoneNumber = userModel.PhoneNumber,
                        EmailConfirmed = !request.RequireEmailConfirmation,
                        RequirePasswordChange = request.RequirePasswordChange,
                        CreatedAt = DateTime.UtcNow,
                        Token = Guid.NewGuid().ToString(),
                        RoleId = ""
                    };

                    // Tạo mật khẩu tạm thời
                    var tempPassword = GenerateTemporaryPassword();
                    var createResult = await _userManager.CreateAsync(user, tempPassword);

                    if (createResult.Succeeded)
                    {
                        // Gán vai trò với kiểm tra bảo mật
                        var role = string.IsNullOrEmpty(userModel.Role) ? request.DefaultRole : userModel.Role;

                        // Chặn vai trò Admin vì lý do bảo mật
                        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Errors.Add($"Dòng {row.RowNumber()}: Vai trò Admin không được phép nhập vì lý do bảo mật. Đã gán vai trò '{request.DefaultRole}'");
                            role = request.DefaultRole;
                        }

                        if (await _roleManager.RoleExistsAsync(role))
                        {
                            await _userManager.AddToRoleAsync(user, role);
                        }
                        else
                        {
                            result.Errors.Add($"Dòng {row.RowNumber()}: Vai trò '{role}' không tồn tại, sử dụng vai trò mặc định '{request.DefaultRole}'");

                            // Gán vai trò mặc định
                            if (await _roleManager.RoleExistsAsync(request.DefaultRole))
                            {
                                await _userManager.AddToRoleAsync(user, request.DefaultRole);
                            }
                        }

                        result.SuccessCount++;
                        result.SuccessMessages.Add($"Tạo thành công: {user.Email}");
                    }
                    else
                    {
                        result.ErrorCount++;
                        result.Errors.Add($"Dòng {row.RowNumber()}: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
                    }
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.Errors.Add($"Dòng {row.RowNumber()}: {ex.Message}");
                }
            }

            result.TotalRows = processedCount;
            return result;
        }

        // Validate email format
        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                // Use MailAddress to validate
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        // Validate username (alphanumeric, underscore, dot only)
        private bool IsValidUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            // Allow letters, numbers, underscore, and dot
            // No spaces or special characters
            return System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_.]+$");
        }

        // Phương thức tạo mật khẩu tạm thời
        private string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz0123456789!@#$%^&*";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 12)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        // Generate username from email (part before @)
        private string GenerateUsernameFromEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
                return "user" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // Extract part before @
            var username = email.Split('@')[0];

            // Remove special characters and dots, keep only alphanumeric and underscore
            username = System.Text.RegularExpressions.Regex.Replace(username, @"[^a-zA-Z0-9_]", "");

            // If empty after cleaning, use fallback
            if (string.IsNullOrWhiteSpace(username))
                return "user" + Guid.NewGuid().ToString("N").Substring(0, 8);

            return username.ToLower();
        }

        // Ensure username is unique by adding numeric suffix if needed
        private async Task<string> EnsureUniqueUsername(string baseUsername)
        {
            var username = baseUsername;
            var counter = 1;

            // Check if base username exists
            while (await _userManager.FindByNameAsync(username) != null)
            {
                username = $"{baseUsername}{counter}";
                counter++;
            }

            return username;
        }

        // GET: Admin/AdminUser/GiftVoucher?userId=xxx
        public async Task<IActionResult> GiftVoucher(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Index");
            }

            // Lấy danh sách promotion codes đang active
            var promotionCodes = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .Where(pc => pc.IsActive && (!pc.ExpiryDate.HasValue || pc.ExpiryDate.Value > DateTime.Now))
                .OrderByDescending(pc => pc.Id)
                .ToListAsync();

            ViewBag.User = user;
            ViewBag.PromotionCodes = promotionCodes;
            return View();
        }

        // POST: Admin/AdminUser/GiftVoucher
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GiftVoucher(string userId, int promotionCodeId, string? note)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Index");
            }

            var promotionCode = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .FirstOrDefaultAsync(pc => pc.Id == promotionCodeId);

            if (promotionCode == null)
            {
                TempData["error"] = "Mã khuyến mãi không tồn tại.";
                return RedirectToAction("GiftVoucher", new { userId });
            }

            if (!promotionCode.IsActive)
            {
                TempData["error"] = "Mã khuyến mãi không còn hiệu lực.";
                return RedirectToAction("GiftVoucher", new { userId });
            }

            // Kiểm tra user đã có voucher này chưa (chưa sử dụng và chưa hết hạn)
            var existingVoucher = await _context.UserVouchers
                .FirstOrDefaultAsync(uv => uv.UserId == userId 
                    && uv.PromotionCodeId == promotionCodeId 
                    && !uv.IsUsed 
                    && uv.ExpiryDate > DateTime.Now);

            if (existingVoucher != null)
            {
                TempData["error"] = "Người dùng đã có voucher này rồi.";
                return RedirectToAction("GiftVoucher", new { userId });
            }

            // Tạo voucher mới
            var userVoucher = new UserVoucher
            {
                UserId = userId,
                PromotionCodeId = promotionCodeId,
                Source = "AdminGift",
                CollectedDate = DateTime.Now,
                ExpiryDate = promotionCode.ExpiryDate ?? promotionCode.Promotion?.EndDate ?? DateTime.Now.AddDays(30),
                IsUsed = false,
                Note = note ?? $"Voucher được tặng bởi Admin vào {DateTime.Now:dd/MM/yyyy HH:mm}"
            };

            _context.UserVouchers.Add(userVoucher);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Đã tặng voucher '{promotionCode.Code}' cho {user.UserName} thành công!";
            return RedirectToAction("Details", new { id = userId });
        }
    }
}