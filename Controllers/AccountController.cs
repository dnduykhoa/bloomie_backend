using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Bloomie.Data;
using Bloomie.Services.Interfaces;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using System.IO.Compression;
using Bloomie.Extensions;

namespace Bloomie.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext context, IEmailService emailService, IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _context = context;
            _emailService = emailService;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpGet]
        public async Task<IActionResult> EnableTwoFactor()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            // Kiểm tra khóa tạm thời
            var lockTimeStr = HttpContext.Session.GetString("TwoFactorLockTime");
            if (!string.IsNullOrEmpty(lockTimeStr) && DateTime.TryParse(lockTimeStr, out var lockTime))
            {
                if ((DateTime.UtcNow - lockTime).TotalMinutes < 10)
                {
                    TempData["error"] = "Bạn đã nhập sai quá số lần cho phép. Vui lòng thử lại sau 10 phút.";
                    return View();
                }
                else
                {
                    HttpContext.Session.Remove("TwoFactorLockTime");
                }
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            var twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            ViewBag.TwoFactorEnabled = twoFactorEnabled;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> EnableTwoFactor(bool enable)
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            // Lấy trạng thái hiện tại của 2FA
            var currentStatus = await _userManager.GetTwoFactorEnabledAsync(user);

            // Nếu trạng thái mới khác trạng thái hiện tại
            if (enable != currentStatus)
            {
                if (enable)
                {
                    // Gửi mã xác thực khi bật 2FA
                    var token = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
                    await _emailService.SendEmailAsync(user.Email, "Xác thực hai bước",
                        $"Mã xác thực hai bước của bạn là: <strong>{token}</strong>. Vui lòng nhập mã này để hoàn tất việc bật xác thực hai bước.");

                    ViewBag.Message = "Một mã xác thực đã được gửi đến email của bạn. Vui lòng kiểm tra và nhập mã.";
                    return View("VerifyTwoFactor");
                }
                else
                {
                    // Tắt 2FA
                    await _userManager.SetTwoFactorEnabledAsync(user, false);
                    TempData["success"] = "Xác thực hai bước đã được tắt thành công.";
                    return RedirectToAction("Profile");
                }
            }

            // Nếu trạng thái không thay đổi
            TempData["info"] = $"Xác thực hai bước đã {(enable ? "được bật" : "bị tắt")} từ trước.";
            return RedirectToAction("Profile");
        }

        [HttpPost]
        public async Task<IActionResult> VerifyTwoFactor(string code)
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            // Kiểm tra khóa tạm thời
            var lockTimeStr = HttpContext.Session.GetString("TwoFactorLockTime");
            if (!string.IsNullOrEmpty(lockTimeStr) && DateTime.TryParse(lockTimeStr, out var lockTime))
            {
                if ((DateTime.UtcNow - lockTime).TotalMinutes < 10)
                {
                    TempData["error"] = "Bạn đã nhập sai quá số lần cho phép. Vui lòng thử lại sau 10 phút.";
                    return RedirectToAction("EnableTwoFactor");
                }
                else
                {
                    HttpContext.Session.Remove("TwoFactorLockTime");
                }
            }

            int failCount = HttpContext.Session.GetInt32("TwoFactorFailCount") ?? 0;
            int maxFail = 5; // Số lần cho phép

            var result = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, code);
            if (result)
            {
                HttpContext.Session.Remove("TwoFactorFailCount");
                HttpContext.Session.Remove("TwoFactorLockTime");
                await _userManager.SetTwoFactorEnabledAsync(user, true);
                TempData["success"] = "Xác thực hai bước đã được bật thành công.";
                ViewBag.TwoFactorEnabled = true;
                return RedirectToAction("Profile");
            }
            else
            {
                failCount++;
                HttpContext.Session.SetInt32("TwoFactorFailCount", failCount);

                if (failCount >= maxFail)
                {
                    HttpContext.Session.SetString("TwoFactorLockTime", DateTime.UtcNow.ToString());
                    HttpContext.Session.Remove("TwoFactorFailCount");
                    TempData["error"] = "Bạn đã nhập sai quá số lần cho phép. Vui lòng thử lại sau 10 phút hoặc yêu cầu gửi lại mã mới.";
                    return RedirectToAction("EnableTwoFactor");
                }

                ViewBag.Error = $"Mã xác thực không đúng. Bạn còn {maxFail - failCount} lần thử.";
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> ResendTwoFactorCode(string userName)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Login");
            }

            var token = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
            await _emailService.SendEmailAsync(user.Email, "Mã xác thực hai bước mới",
                $"Mã xác thực hai bước mới của bạn là: <strong>{token}</strong>. Mã này sẽ hết hạn sau 5 phút.");

            TempData["info"] = "Mã xác thực mới đã được gửi lại.";
            return View("TwoFactorLogin", new TwoFactorViewModel { UserName = user.UserName });
        }

        [HttpPost]
        public async Task<IActionResult> TwoFactorLogin(TwoFactorViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Không tìm thấy người dùng.");
                return View(model);
            }

            // Đếm số lần nhập sai
            int failCount = HttpContext.Session.GetInt32("TwoFactorLoginFailCount") ?? 0;
            int maxFail = 5;

            var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, model.TwoFactorCode);

            if (isValid)
            {
                HttpContext.Session.Remove("TwoFactorLoginFailCount");
                HttpContext.Session.Remove("TwoFactorLoginLockTime");
                await _signInManager.SignInAsync(user, isPersistent: false);

                if (await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    return RedirectToAction("Index", "Home", new { area = "Admin" });
                }
                else if (await _userManager.IsInRoleAsync(user, "Staff"))
                {
                    return RedirectToAction("Index", "Home", new { area = "Staff" });
                }
                else
                {
                    return RedirectToAction("Index", "Home");
                }
            }

            // Nếu mã không đúng
            failCount++;
            HttpContext.Session.SetInt32("TwoFactorLoginFailCount", failCount);

            if (failCount >= maxFail)
            {
                // Khóa tài khoản trong 30 phút
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(30));
                HttpContext.Session.Remove("TwoFactorLoginFailCount");
                TempData["error"] = "Bạn đã nhập sai quá nhiều lần. Tài khoản của bạn đã bị khóa trong 30 phút.";
                await _signInManager.SignOutAsync();
                return RedirectToAction("Login");
            }

            ModelState.AddModelError(string.Empty, $"Mã xác thực không đúng. Bạn còn {maxFail - failCount} lần thử.");
            return View(model);
        }

        public IActionResult Login(string returnUrl)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel loginVM)
        {
            if (!ModelState.IsValid)
            {
                return View(loginVM);
            }
            
            var user = await _userManager.FindByNameAsync(loginVM.UserName);
            if (user != null && user.IsDeleted)
            {
                TempData["error"] = "Tài khoản của bạn đã bị xóa tạm thời. Nếu muốn khôi phục, hãy nhấn 'Yêu cầu khôi phục tài khoản'.";
                return View(loginVM);
            }

            if (user != null && !user.LockoutEnabled)
            {
                user.LockoutEnabled = true;
                await _userManager.UpdateAsync(user);
            }

            // Chỉ cho phép role 'User' đăng nhập qua trang này
            var userRoles = user != null ? await _userManager.GetRolesAsync(user) : new List<string>();
            if (userRoles.Any(r => r == "Admin" || r == "Staff" || r == "Manager"))
            {
                ModelState.AddModelError("", "Tài khoản quản trị không được phép đăng nhập tại đây. Vui lòng dùng trang quản trị.");
                return View(loginVM);
            }

            var result = await _signInManager.PasswordSignInAsync(loginVM.UserName, loginVM.Password, loginVM.RememberMe, lockoutOnFailure: true);

            if (result.RequiresTwoFactor)
            {
                // Gửi mã 2FA qua email
                user = await _userManager.FindByNameAsync(loginVM.UserName);
                var token = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
                await _emailService.SendEmailAsync(user.Email, "Mã xác thực hai bước",
                    $"Mã xác thực hai bước của bạn là: <strong>{token}</strong>. Vui lòng nhập mã này trong vòng 5 phút.");
                return View("TwoFactorLogin", new TwoFactorViewModel { UserName = loginVM.UserName });
            }
            else if (result.Succeeded)
            {
                user = await _userManager.FindByNameAsync(loginVM.UserName);
                // Comment kiểm tra xác thực email để cho phép đăng nhập luôn
                // if (user != null && !await _userManager.IsInRoleAsync(user, "Admin") && !await _userManager.IsEmailConfirmedAsync(user))
                // {
                //     ModelState.AddModelError("", "Bạn cần xác thực email trước khi đăng nhập.");
                //     return View(loginVM);
                // }

                // Lưu lịch sử đăng nhập
                var ip = GetClientIpAddress();
                var userAgent = Request.Headers["User-Agent"].ToString();
                var (browser, os, deviceType) = ParseUserAgent(userAgent);

                // Lấy location từ IP
                var location = await GetLocationFromIP(ip);

                // Kiểm tra thiết bị mới (ví dụ: so sánh userAgent + IP với các bản ghi trước)
                bool isNewDevice = await IsNewDevice(user.Id, userAgent, ip);

                var sessionId = Guid.NewGuid().ToString();
                HttpContext.Session.SetString("SessionId", sessionId);

                _context.LoginHistories.Add(new LoginHistory
                {
                    UserId = user.Id,
                    LoginTime = DateTime.UtcNow,
                    IPAddress = ip,
                    UserAgent = userAgent,
                    Browser = browser,
                    IsNewDevice = isNewDevice,
                    SessionId = sessionId,
                    Location = location
                });
                await _context.SaveChangesAsync();

                // Nếu là thiết bị mới, gửi thông báo email
                if (isNewDevice)
                {
                    // Lấy thông tin vị trí từ IP
                    var deviceLocation = await GetLocationFromIP(ip);

                    // Đếm số lần đăng nhập từ thiết bị này
                    var loginCount = await _context.LoginHistories
                        .CountAsync(h => h.UserId == user.Id && h.UserAgent == userAgent && h.IPAddress == ip);

                    // Tạo token bảo mật cho link đăng xuất
                    var loginHistory = new LoginHistory { SessionId = sessionId, UserId = user.Id };
                    var logoutToken = loginHistory.GenerateSecurityToken();
                    var logoutLink = $"{Request.Scheme}://{Request.Host}/Account/LogoutDeviceFromEmail?sessionId={sessionId}&token={logoutToken}";

                    var emailContent = $@"
                    <!DOCTYPE html>
                    <html lang='vi'>
                    <head>
                        <meta charset='UTF-8'>
                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                        <style>
                            body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
                            .container {{ max-width: 600px; margin: 20px auto; background-color: #ffffff; border-radius: 10px; box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1); overflow: hidden; }}
                            .header {{ background-color: #FF7043; padding: 20px; text-align: center; }}
                            .header h1 {{ color: #ffffff; margin: 0; }}
                            .content {{ padding: 30px; color: #333; }}
                            .alert-box {{ background-color: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }}
                            .device-info {{ background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 15px 0; }}
                            .btn-danger {{ display: inline-block; padding: 12px 24px; background-color: #dc3545; color: #ffffff !important; text-decoration: none; font-size: 16px; font-weight: bold; border-radius: 5px; margin: 10px 5px; }}
                            .btn-primary {{ display: inline-block; padding: 12px 24px; background-color: #007bff; color: #ffffff !important; text-decoration: none; font-size: 16px; font-weight: bold; border-radius: 5px; margin: 10px 5px; }}
                            .footer {{ background-color: #f8f8f8; padding: 15px; text-align: center; font-size: 14px; color: #777; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <div class='header'>
                                <h1>🔐 Bloomie Shop - Cảnh báo bảo mật</h1>
                            </div>
                            <div class='content'>
                                <h2>Đăng nhập từ thiết bị mới</h2>
                                <div class='alert-box'>
                                    <strong>⚠️ Cảnh báo:</strong> Tài khoản của bạn vừa đăng nhập từ một thiết bị mới.
                                </div>
                
                                <p>Xin chào <strong>{user.FullName}</strong>,</p>
                                <p>Chúng tôi phát hiện một đăng nhập mới vào tài khoản Bloomie của bạn:</p>
                
                                <div class='device-info'>
                                    <strong>📱 Thông tin thiết bị:</strong><br/>
                                    🕐 <strong>Thời gian:</strong> {DateTime.UtcNow:HH:mm dd/MM/yyyy} (GMT+7)<br/>
                                    📍 <strong>Vị trí:</strong> {deviceLocation}<br/>
                                    🌐 <strong>Địa chỉ IP:</strong> {ip}<br/>
                                    {deviceType} <strong>Loại thiết bị:</strong> {deviceType.Replace("� ", "").Replace("�💻 ", "")}<br/>
                                    🌐 <strong>Trình duyệt:</strong> {browser}<br/>
                                    💻 <strong>Hệ điều hành:</strong> {os}<br/>
                                    📊 <strong>Lần đăng nhập thứ:</strong> {loginCount} từ thiết bị này<br/>
                                    🆔 <strong>Session:</strong> {sessionId.Substring(0, 8)}...
                                </div>
                
                                <h3>🤔 Đây có phải là bạn không?</h3>
                                <p><strong>Nếu ĐÚNG là bạn:</strong> Bạn có thể bỏ qua email này. Tài khoản của bạn an toàn.</p>
                                <p><strong>Nếu KHÔNG phải bạn:</strong> Hãy thực hiện ngay các bước sau:</p>
                
                                <div style='text-align: center; margin: 25px 0;'>
                                    <a href='{logoutLink}' class='btn-danger'>
                                        🚫 ĐĂNG XUẤT THIẾT BỊ NÀY NGAY
                                    </a>
                                    <br/>
                                    <a href='{Request.Scheme}://{Request.Host}/Account/UpdateAccount' class='btn-primary'>
                                        🔑 ĐỔI MẬT KHẨU
                                    </a>
                                </div>
                
                                <div class='alert-box'>
                                    <strong>💡 Lưu ý:</strong> Link đăng xuất thiết bị chỉ có hiệu lực trong 24 giờ và chỉ sử dụng được 1 lần.
                                </div>
                
                                <p>Nếu bạn cần hỗ trợ, vui lòng liên hệ:</p>
                                <p>📞 Hotline: <strong>0987 654 321</strong><br/>
                                📧 Email: <strong>bloomieshop25@gmail.com</strong></p>
                            </div>
                            <div class='footer'>
                                <p>© 2025 Bloomie Flower Shop. Email này được gửi tự động, vui lòng không trả lời.</p>
                                <p>🔒 Bảo mật tài khoản là ưu tiên hàng đầu của chúng tôi.</p>
                            </div>
                        </div>
                    </body>
                    </html>";

                    await _emailService.SendEmailAsync(user.Email, "🔐 Cảnh báo: Đăng nhập từ thiết bị mới", emailContent);
                }

                if (user.RequirePasswordChange)
                {
                    return RedirectToAction("NewPassword", "Account", new { email = user.Email, token = user.Token });
                }

                if (await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    return Redirect(loginVM.ReturnUrl ?? "/Admin/AdminDashboard");
                }
                else if (await _userManager.IsInRoleAsync(user, "Staff"))
                {
                    return Redirect(loginVM.ReturnUrl ?? "/Staff/StaffDashboard");
                }
                
                TempData["SuccessMessage"] = $"Chào mừng {user.FullName ?? user.UserName} đã quay trở lại! 🌸";
                return Redirect(loginVM.ReturnUrl ?? "/Home/Index");
            }
            else if (result.IsLockedOut)
            {
                var lockoutEnd = await _userManager.GetLockoutEndDateAsync(user);
                if (lockoutEnd == DateTimeOffset.MaxValue)
                {
                    // Khóa vĩnh viễn
                    ModelState.AddModelError("", "Tài khoản đã bị khóa vĩnh viễn. Vui lòng liên hệ admin.");
                }
                else
                {
                    // Khóa có thời hạn - hiển thị thông minh
                    var timeLeft = lockoutEnd - DateTimeOffset.UtcNow;
                    string timeMessage = "";

                    if (timeLeft?.TotalDays >= 1)
                    {
                        timeMessage = $"Còn {(int)timeLeft.Value.TotalDays} ngày {timeLeft.Value.Hours} giờ";
                    }
                    else if (timeLeft?.TotalHours >= 1)
                    {
                        timeMessage = $"Còn {timeLeft.Value.Hours} giờ {timeLeft.Value.Minutes} phút";
                    }
                    else if (timeLeft?.TotalMinutes >= 1)
                    {
                        timeMessage = $"Còn {timeLeft.Value.Minutes} phút {timeLeft.Value.Seconds} giây";
                    }
                    else if (timeLeft?.TotalSeconds > 0)
                    {
                        timeMessage = $"Còn {timeLeft.Value.Seconds} giây";
                    }
                    else
                    {
                        timeMessage = "Đã hết hạn khóa";
                    }

                    ModelState.AddModelError("", $"Tài khoản bị khóa đến {lockoutEnd:dd/MM/yyyy HH:mm}. {timeMessage}.");
                }
            }
            else
            {
                ModelState.AddModelError("", "Tên đăng nhập hoặc mật khẩu không đúng.");
            }
            return View(loginVM);
        }

        public async Task<IActionResult> LoginHistory()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sessions = await _context.LoginHistories
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.LoginTime)
                .ToListAsync();
            var currentSessionId = HttpContext.Session.GetString("SessionId");
            ViewBag.CurrentSessionId = currentSessionId;

            return View(sessions);
        }

        [HttpPost]
        public async Task<IActionResult> LogoutSession(string sessionId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentSessionId = HttpContext.Session.GetString("SessionId");

            // Kiểm tra xem có phải là phiên hiện tại không
            if (sessionId == currentSessionId)
            {
                TempData["error"] = "Không thể đăng xuất phiên hiện tại. Vui lòng sử dụng nút Đăng xuất nếu muốn kết thúc phiên này.";
                return RedirectToAction("LoginHistory");
            }

            var session = await _context.LoginHistories.FirstOrDefaultAsync(h => h.SessionId == sessionId && h.UserId == userId);
            if (session != null)
            {
                _context.LoginHistories.Remove(session);
                await _context.SaveChangesAsync();
                TempData["success"] = "Đã đăng xuất khỏi thiết bị thành công.";
            }
            return RedirectToAction("LoginHistory");
        }

        [HttpPost]
        public async Task<IActionResult> LogoutAllSessions()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentSessionId = HttpContext.Session.GetString("SessionId");
            var sessions = await _context.LoginHistories
                .Where(h => h.UserId == userId)
                .ToListAsync();
            _context.LoginHistories.RemoveRange(sessions);
            await _context.SaveChangesAsync();
            await _signInManager.SignOutAsync();
            HttpContext.Session.Clear();
            TempData["success"] = "Đăng xuất khỏi tất cả thiết bị thành công.";
            return RedirectToAction("Login");
        }

        // Khởi tạo đăng nhập bằng Google
        public async Task LoginByGoogle()
        {
            await HttpContext.ChallengeAsync(GoogleDefaults.AuthenticationScheme,
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action("GoogleResponse")
                });
        }

        public async Task<IActionResult> GoogleResponse()
        {
            // Xác thực kết quả từ Google
            var result = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
            if (!result.Succeeded)
            {
                return RedirectToAction("Login");
            }

            // Lấy claims
            var claims = result.Principal?.Identities.FirstOrDefault()?.Claims.Select(claim => new
            {
                claim.Issuer, // Ai phát hành claim
                claim.OriginalIssuer, // Nơi claim đc tạo
                claim.Type,
                claim.Value
            });

            // Lấy email từ claims
            var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            string emailName = email.Split('@')[0];

            // Kiểm tra người dùng đã tồn tại
            var existingUser = await _userManager.FindByEmailAsync(email);

            if (existingUser == null)
            {
                // Đảm bảo vai trò "User" tồn tại
                if (!await _roleManager.RoleExistsAsync("User"))
                {
                    await _roleManager.CreateAsync(new IdentityRole { Name = "User", NormalizedName = "USER" });
                }

                // Lấy vai trò "User"
                var userRole = await _roleManager.FindByNameAsync("User");

                string token = Guid.NewGuid().ToString();

                var newUser = new ApplicationUser
                {
                    UserName = emailName,
                    Email = email,
                    FullName = emailName,
                    RoleId = userRole?.Id,
                    Token = token,
                    LockoutEnabled = true
                };

                // Tạo người dùng trong cơ sở dữ liệu
                var createUserResult = await _userManager.CreateAsync(newUser);
                if (!createUserResult.Succeeded)
                {
                    TempData["error"] = "Đăng ký tài khoản thất bại. Vui lòng thử lại sau: " + string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                    return RedirectToAction("Login", "Account");
                }

                // Gán vai trò User
                var roleResult = await _userManager.AddToRoleAsync(newUser, "User");
                if (!roleResult.Succeeded)
                {
                    TempData["error"] = "Gán vai trò thất bại. Vui lòng thử lại sau: " + string.Join(", ", roleResult.Errors.Select(e => e.Description));
                    return RedirectToAction("Login", "Account");
                }

                await _userManager.ConfirmEmailAsync(newUser, await _userManager.GenerateEmailConfirmationTokenAsync(newUser));

                // Tự động tặng voucher chào mừng cho thành viên mới
                await GiveWelcomeVoucherAsync(newUser.Id);

                // Gửi email chào mừng với liên kết đặt mật khẩu
                await _emailService.SendEmailAsync(newUser.Email, "Chào mừng đến với Bloomie",
                    $"Chào {newUser.FullName},<br/>Tài khoản của bạn đã được tạo thành công! " +
                    $"<p><strong>🎁 Chúc mừng! Bạn đã nhận được voucher chào mừng thành viên mới!</strong></p>" +
                    $"Bạn có thể tiếp tục sử dụng Google để đăng nhập. Nếu muốn thiết lập mật khẩu để đăng nhập bằng email, " +
                    $"<a href='{Request.Scheme}://{Request.Host}/Account/SetNewPassword?email={newUser.Email}&token={token}'>nhấn vào đây</a> để đặt mật khẩu. Liên kết này sẽ hết hạn sau 24 giờ.");

                // Ghi nhận lịch sử đăng nhập cho user mới
                var ip = GetClientIpAddress();
                var userAgent = Request.Headers["User-Agent"].ToString();
                var sessionId = Guid.NewGuid().ToString();
                var (browser, os, deviceType) = ParseUserAgent(userAgent);
                var location = await GetLocationFromIP(ip);
                HttpContext.Session.SetString("SessionId", sessionId);

                _context.LoginHistories.Add(new LoginHistory
                {
                    UserId = newUser.Id,
                    LoginTime = DateTime.UtcNow,
                    IPAddress = ip,
                    UserAgent = userAgent,
                    IsNewDevice = true,
                    Browser = browser,
                    SessionId = sessionId,
                    Location = location
                });
                await _context.SaveChangesAsync();

                await _signInManager.SignInAsync(newUser, isPersistent: false);
                TempData["SuccessMessage"] = $"Chào mừng {newUser.FullName} đến với Bloomie! 🌸 Bạn đã nhận được voucher chào mừng!";
                return RedirectToAction("Index", "Home");
            }
            else
            {
                if (existingUser.IsDeleted)
                {
                    TempData["error"] = "Tài khoản của bạn đã bị xóa tạm thời. Nếu muốn khôi phục, hãy nhấn 'Yêu cầu khôi phục tài khoản'.";
                    return RedirectToAction("Login");
                }

                if (!existingUser.LockoutEnabled)
                {
                    existingUser.LockoutEnabled = true;
                    await _userManager.UpdateAsync(existingUser);
                }

                // Chặn nếu account đang bị khóa (lockout thực sự)
                if (await _userManager.IsLockedOutAsync(existingUser))
                {
                    var lockoutEnd = await _userManager.GetLockoutEndDateAsync(existingUser);
                    TempData["error"] = lockoutEnd == DateTimeOffset.MaxValue
                    ? "Tài khoản đã bị khóa vĩnh viễn. Vui lòng liên hệ admin."
                    : $"Tài khoản hiện đang bị khóa đến {lockoutEnd:dd/MM/yyyy HH:mm}.";
                    return RedirectToAction("Login", "Account");
                }

                // Ghi nhận lịch sử đăng nhập cho user hiện tại
                var currentIp = GetClientIpAddress();
                var currentUserAgent = Request.Headers["User-Agent"].ToString();
                var currentSessionId = Guid.NewGuid().ToString();

                bool isNewDevice = !await _context.LoginHistories.AnyAsync(h =>
                h.UserId == existingUser.Id &&
                h.UserAgent == currentUserAgent &&
                h.IPAddress == currentIp);

                HttpContext.Session.SetString("SessionId", currentSessionId);

                // Parse browser from user agent
                var (browser, os, deviceType) = ParseUserAgent(currentUserAgent);
                var location = await GetLocationFromIP(currentIp);

                _context.LoginHistories.Add(new LoginHistory
                {
                    UserId = existingUser.Id,
                    LoginTime = DateTime.UtcNow,
                    IPAddress = currentIp,
                    UserAgent = currentUserAgent,
                    IsNewDevice = isNewDevice,
                    Browser = browser,
                    SessionId = currentSessionId,
                    Location = location
                });
                await _context.SaveChangesAsync();

                await _signInManager.SignInAsync(existingUser, isPersistent: false);
                TempData["SuccessMessage"] = $"Chào mừng {existingUser.FullName ?? existingUser.UserName} đã quay trở lại! 🌸";
                return RedirectToAction("Index", "Home");
            }
        }

        public async Task LoginByFacebook()
        {
            await HttpContext.ChallengeAsync("Facebook",
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action("FacebookResponse")
                });
        }

        public async Task<IActionResult> FacebookResponse()
        {
            try
            {
                var result = await HttpContext.AuthenticateAsync("Facebook");
                if (!result.Succeeded)
                {
                    TempData["error"] = "Đăng nhập bằng Facebook thất bại. Chi tiết: " + (result.Failure?.Message ?? "Không có chi tiết lỗi.");
                    return RedirectToAction("Login", "Account");
                }

                var claims = result.Principal?.Identities.FirstOrDefault()?.Claims;
                var email = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
                var fullName = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

                if (string.IsNullOrEmpty(email))
                {
                    TempData["error"] = "Không thể lấy thông tin email từ Facebook.";
                    return RedirectToAction("Login", "Account");
                }

                string emailName = email.Split('@')[0];
                var existingUser = await _userManager.FindByEmailAsync(email);

                if (existingUser == null)
                {
                    if (!await _roleManager.RoleExistsAsync("User"))
                    {
                        await _roleManager.CreateAsync(new IdentityRole { Name = "User", NormalizedName = "USER" });
                    }

                    var userRole = await _roleManager.FindByNameAsync("User");
                    string token = Guid.NewGuid().ToString();

                    var newUser = new ApplicationUser
                    {
                        UserName = emailName,
                        Email = email,
                        FullName = fullName ?? emailName,
                        RoleId = userRole?.Id,
                        Token = token
                    };

                    var createUserResult = await _userManager.CreateAsync(newUser);
                    if (!createUserResult.Succeeded)
                    {
                        TempData["error"] = "Đăng ký tài khoản thất bại: " + string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                        return RedirectToAction("Login", "Account");
                    }

                    var roleResult = await _userManager.AddToRoleAsync(newUser, "User");
                    if (!roleResult.Succeeded)
                    {
                        TempData["error"] = "Gán vai trò thất bại: " + string.Join(", ", roleResult.Errors.Select(e => e.Description));
                        return RedirectToAction("Login", "Account");
                    }

                    // Tự động tặng voucher chào mừng cho thành viên mới
                    await GiveWelcomeVoucherAsync(newUser.Id);

                    // Ghi nhận lịch sử đăng nhập
                    var ip = GetClientIpAddress();
                    var userAgent = Request.Headers["User-Agent"].ToString();
                    var sessionId = Guid.NewGuid().ToString();
                    var (browser, os, deviceType) = ParseUserAgent(userAgent);
                    var location = await GetLocationFromIP(ip);
                    HttpContext.Session.SetString("SessionId", sessionId);

                    _context.LoginHistories.Add(new LoginHistory
                    {
                        UserId = newUser.Id,
                        LoginTime = DateTime.UtcNow,
                        IPAddress = ip,
                        UserAgent = userAgent,
                        IsNewDevice = true,
                        Browser = browser,
                        SessionId = sessionId,
                        Location = location
                    });
                    await _context.SaveChangesAsync();

                    await _signInManager.SignInAsync(newUser, isPersistent: false);
                    TempData["SuccessMessage"] = $"Chào mừng {newUser.FullName} đến với Bloomie! 🌸 Bạn đã nhận được voucher chào mừng!";
                    return RedirectToAction("Index", "Home");
                }
                else
                {
                    if (existingUser.IsDeleted)
                    {
                        TempData["error"] = "Tài khoản đã bị xóa.";
                        return RedirectToAction("Login");
                    }
                    if (await _userManager.IsLockedOutAsync(existingUser))
                    {
                        TempData["error"] = "Tài khoản bị khóa.";
                        return RedirectToAction("Login");
                    }

                    // Ghi nhận lịch sử đăng nhập
                    var ip = GetClientIpAddress();
                    var userAgent = Request.Headers["User-Agent"].ToString();
                    var sessionId = Guid.NewGuid().ToString();
                    var (browser, os, deviceType) = ParseUserAgent(userAgent);
                    var location = await GetLocationFromIP(ip);
                    HttpContext.Session.SetString("SessionId", sessionId);

                    _context.LoginHistories.Add(new LoginHistory
                    {
                        UserId = existingUser.Id,
                        LoginTime = DateTime.UtcNow,
                        IPAddress = ip,
                        UserAgent = userAgent,
                        IsNewDevice = true,
                        Browser = browser,
                        SessionId = sessionId,
                        Location = location
                    });
                    await _context.SaveChangesAsync();

                    await _signInManager.SignInAsync(existingUser, isPersistent: false);
                    TempData["SuccessMessage"] = $"Chào mừng {existingUser.FullName ?? existingUser.UserName} đã quay trở lại! 🌸";
                    return RedirectToAction("Index", "Home");
                }
            }
            catch (Exception ex)
            {
                TempData["error"] = "Đã xảy ra lỗi khi xử lý đăng nhập Facebook: " + ex.Message;
                return RedirectToAction("Login", "Account");
            }
        }

        public async Task LoginByTwitter()
        {
            await HttpContext.ChallengeAsync("Twitter",
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action("TwitterResponse")
                });
        }

        public async Task<IActionResult> TwitterResponse()
        {
            var result = await HttpContext.AuthenticateAsync("Twitter");
            if (!result.Succeeded)
            {
                TempData["error"] = "Đăng nhập bằng Twitter thất bại. Chi tiết: " + (result.Failure?.Message ?? "Không có chi tiết lỗi.");
                return RedirectToAction("Login", "Account");
            }

            var claims = result.Principal?.Identities.FirstOrDefault()?.Claims;
            var email = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            var fullName = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(email))
            {
                email = claims?.FirstOrDefault(c => c.Type == "urn:twitter:username")?.Value + "@twitter.com";
                if (string.IsNullOrEmpty(email))
                {
                    TempData["error"] = "Không thể lấy thông tin email từ Twitter.";
                    return RedirectToAction("Login", "Account");
                }
            }

            string emailName = email.Split('@')[0];
            var existingUser = await _userManager.FindByEmailAsync(email);

            if (existingUser == null)
            {
                if (!await _roleManager.RoleExistsAsync("User"))
                {
                    await _roleManager.CreateAsync(new IdentityRole { Name = "User", NormalizedName = "USER" });
                }

                var userRole = await _roleManager.FindByNameAsync("User");
                string token = Guid.NewGuid().ToString();

                var newUser = new ApplicationUser
                {
                    UserName = emailName,
                    Email = email,
                    FullName = fullName ?? emailName,
                    RoleId = userRole?.Id,
                    Token = token
                };

                var createUserResult = await _userManager.CreateAsync(newUser);
                if (!createUserResult.Succeeded)
                {
                    TempData["error"] = "Đăng ký tài khoản thất bại: " + string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                    return RedirectToAction("Login", "Account");
                }

                var roleResult = await _userManager.AddToRoleAsync(newUser, "User");
                if (!roleResult.Succeeded)
                {
                    TempData["error"] = "Gán vai trò thất bại: " + string.Join(", ", roleResult.Errors.Select(e => e.Description));
                    return RedirectToAction("Login", "Account");
                }

                // Tự động tặng voucher chào mừng cho thành viên mới
                await GiveWelcomeVoucherAsync(newUser.Id);

                await _emailService.SendEmailAsync(newUser.Email, "Chào mừng đến với Bloomie",
                    $"Chào {newUser.FullName},<br/>Tài khoản của bạn đã được tạo. " +
                    $"<p><strong>🎁 Chúc mừng! Bạn đã nhận được voucher chào mừng thành viên mới!</strong></p>" +
                    $"Vui lòng <a href='{Request.Scheme}://{Request.Host}/Account/SetNewPassword?email={newUser.Email}&token={token}'>nhấp vào đây</a> để đặt mật khẩu mới.");

                TempData["success"] = "Đăng ký bằng Twitter thành công. Vui lòng đặt mật khẩu mới.";
                return RedirectToAction("SetNewPassword", new { email = newUser.Email, token = token });
            }
            else
            {
                await _signInManager.SignInAsync(existingUser, isPersistent: false);
                TempData["success"] = "Đăng nhập bằng Twitter thành công.";
                return RedirectToAction("Index", "Home");
            }
        }

        // public async Task<IActionResult> SetNewPassword(string email, string token)
        // {
        //     // Kiểm tra email và token
        //     var user = await _userManager.Users
        //         .FirstOrDefaultAsync(u => u.Email == email && u.Token == token);

        //     if (user == null)
        //     {
        //         TempData["error"] = "Liên kết không hợp lệ.";
        //         return RedirectToAction("Login");
        //     }

        //     ViewBag.Email = email;
        //     ViewBag.Token = token;
        //     return View();
        // }

        // [HttpPost]
        // public async Task<IActionResult> SetNewPassword(ApplicationUser model, string token)
        // {
        //     var user = await _userManager.Users
        //         .FirstOrDefaultAsync(u => u.Email == model.Email && u.Token == token);

        //     if (user == null)
        //     {
        //         // Nếu không tìm thấy người dùng với email và token, trả về thông báo lỗi
        //         TempData["error"] = "Liên kết không hợp lệ.";
        //         return RedirectToAction("Login");
        //     }

        //     if (string.IsNullOrEmpty(model.PasswordHash))
        //     {
        //         ModelState.AddModelError("PasswordHash", "Vui lòng nhập mật khẩu");
        //         ViewBag.Email = user.Email;
        //         ViewBag.Token = token;
        //         return View(model);
        //     }

        //     try
        //     {
        //         // Set password mới
        //         var passwordHasher = new PasswordHasher<ApplicationUser>();
        //         var passwordHash = passwordHasher.HashPassword(user, model.PasswordHash);
        //         user.PasswordHash = passwordHash;

        //         // Set username là phần trước @ của email
        //         string username = user.Email.Split('@')[0];
        //         user.UserName = username;

        //         // Cập nhật token mới
        //         user.Token = Guid.NewGuid().ToString();
        //         var result = await _userManager.UpdateAsync(user);

        //         if (result.Succeeded)
        //         {
        //             await _signInManager.SignInAsync(user, isPersistent: false);
        //             TempData["success"] = $"Đặt mật khẩu thành công! Bạn có thể đăng nhập bằng tên đăng nhập '{username}' hoặc email '{user.Email}'";
        //             return RedirectToAction("Index", "Home");
        //         }
        //         else
        //         {
        //             foreach (var error in result.Errors)
        //             {
        //                 ModelState.AddModelError("", error.Description);
        //             }
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         ModelState.AddModelError("", "Có lỗi xảy ra: " + ex.Message);
        //     }

        //     ViewBag.Email = user.Email;
        //     ViewBag.Token = token;
        //     return View(model);
        // }

        // [HttpPost]
        // public async Task<IActionResult> SetNewPassword(string email, string token, string newPassword)
        // {
        //     var user = await _userManager.Users
        //         .FirstOrDefaultAsync(u => u.Email == email);

        //     if (user == null)
        //     {
        //         TempData["error"] = "Không tìm thấy tài khoản.";
        //         return RedirectToAction("Login");
        //     }

        //     // Reset password và mở khóa
        //     var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        //     var result = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);

        //     if (result.Succeeded)
        //     {
        //         // Mở khóa tài khoản
        //         await _userManager.SetLockoutEndDateAsync(user, null);
        //         await _userManager.UpdateSecurityStampAsync(user);

        //         TempData["success"] = "Đặt lại mật khẩu thành công. Tài khoản đã được mở khóa.";
        //         return RedirectToAction("Login");
        //     }

        //     foreach (var error in result.Errors)
        //     {
        //         ModelState.AddModelError("", error.Description);
        //     }

        //     return View();
        // }

        public IActionResult Register()
        {
            return View(new UserViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Register(UserViewModel user)
        {
            if (ModelState.IsValid)
            {
                var existingUser = await _userManager.FindByNameAsync(user.UserName);
                if (existingUser != null)
                {
                    ModelState.AddModelError("UserName", "Tên đăng nhập đã tồn tại. Vui lòng chọn tên đăng nhập khác.");
                    return View(user);
                }

                if (!await _roleManager.RoleExistsAsync("User"))
                {
                    await _roleManager.CreateAsync(new IdentityRole { Name = "User", NormalizedName = "USER" });
                }

                var userRole = await _roleManager.FindByNameAsync("User");

                var newUser = new ApplicationUser
                {
                    FullName = user.FullName,
                    UserName = user.UserName,
                    Email = user.Email,
                    RoleId = userRole?.Id,
                    Token = string.Empty
                };

                var result = await _userManager.CreateAsync(newUser, user.Password);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(newUser, "User");

                    // Tự động tặng voucher chào mừng cho thành viên mới
                    await GiveWelcomeVoucherAsync(newUser.Id);

                    // Gửi email xác thực
                    var token = await _userManager.GenerateEmailConfirmationTokenAsync(newUser);
                    var confirmationLink = Url.Action("ConfirmEmail", "Account", new { userId = newUser.Id, token = token }, Request.Scheme);

                    await _emailService.SendEmailAsync(newUser.Email, "Xác thực email Bloomie",
                        $"<p>Chào {newUser.FullName},</p><p>Vui lòng <a href='{confirmationLink}'>nhấn vào đây</a> để xác thực email và kích hoạt tài khoản Bloomie.</p><p><strong>🎁 Chúc mừng! Bạn đã nhận được voucher chào mừng thành viên mới!</strong></p>");

                    TempData["success"] = "Đăng ký thành công! Bạn đã nhận voucher chào mừng. Vui lòng kiểm tra email để xác thực tài khoản.";
                    return RedirectToAction("Login");
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
            return View(user);
        }

        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            if (userId == null || token == null)
            {
                TempData["error"] = "Liên kết xác thực không hợp lệ.";
                return RedirectToAction("Login");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Login");
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                TempData["success"] = "Xác thực email thành công! Bạn có thể đăng nhập.";
            }
            else
            {
                TempData["error"] = "Xác thực email thất bại hoặc liên kết đã hết hạn.";
            }
            return RedirectToAction("Login");
        }

        public async Task<IActionResult> Logout(string returnUrl = "/")
        {
            // Xóa tất cả cookie xác thực
            await HttpContext.SignOutAsync();
            await _signInManager.SignOutAsync();
            return Redirect(returnUrl);
        }

        public async Task<IActionResult> NewPassword(string email, string token)
        {
            // Kiểm tra email và token
            var checkuser = await _userManager.Users
                .Where(u => u.Email == email)
                .Where(u => u.Token == token).FirstOrDefaultAsync();

            if (checkuser != null)
            {
                ViewBag.Email = checkuser.Email;
                ViewBag.Token = token;
            }
            else
            {
                TempData["error"] = "Không tìm thấy email hoặc token không đúng";
                return RedirectToAction("ForgotPassword", "Account");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UpdateNewPassword(ApplicationUser user, string token)
        {
            var checkuser = await _userManager.Users
                .Where(u => u.Email == user.Email)
                .Where(u => u.Token == user.Token).FirstOrDefaultAsync();

            if (checkuser != null)
            {
                string newtoken = Guid.NewGuid().ToString();

                // Sử dụng API Identity để kiểm tra mật khẩu
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(checkuser);
                var result = await _userManager.ResetPasswordAsync(checkuser, resetToken, user.PasswordHash);

                if (result.Succeeded)
                {
                    checkuser.RequirePasswordChange = false;
                    checkuser.Token = Guid.NewGuid().ToString();
                    await _userManager.UpdateAsync(checkuser);
                    TempData["success"] = "Mật khẩu cập nhật thành công.";
                    return RedirectToAction("Login", "Account");
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    ViewBag.Email = user.Email;
                    ViewBag.Token = user.Token;
                    return View("NewPassword", user);
                }
            }
            else
            {
                TempData["error"] = "Không tìm thấy email hoặc token không đúng";
                return RedirectToAction("ForgotPassword", "Account");
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendMailForgotPass(ApplicationUser user)
        {
            int forgotCount = HttpContext.Session.GetInt32("ForgotPassCount") ?? 0;
            if (forgotCount >= 5)
            {
                TempData["error"] = "Bạn đã yêu cầu đặt lại mật khẩu quá nhiều lần. Vui lòng thử lại sau 1 giờ.";
                return RedirectToAction("ForgotPassword", "Account");
            }
            HttpContext.Session.SetInt32("ForgotPassCount", forgotCount + 1);

            // Kiểm tra email tồn tại
            var checkMail = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == user.Email);

            if (checkMail == null)
            {
                TempData["error"] = "Email không tồn tại";
                return RedirectToAction("ForgotPassword", "Account");
            }
            else
            {
                string token = Guid.NewGuid().ToString();
                checkMail.Token = token;
                _context.Update(checkMail);
                await _context.SaveChangesAsync();

                var receiver = checkMail.Email;
                var subject = "Đặt lại mật khẩu cho tài khoản Bloomie Flower Shop";

                // Tạo URL để đổi mật khẩu
                var resetLink = $"{Request.Scheme}://{Request.Host}/Account/NewPassword?email={checkMail.Email}&token={token}";

                // Template HTML cho email
                var message = $@"
                <!DOCTYPE html>
                <html lang='vi'>
                <head>
                    <meta charset='UTF-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <style>
                    body {{
                        font-family: Arial, sans-serif;
                        background-color: #f4f4f4;
                        margin: 0;
                        padding: 0;
                    }}

                    .container {{
                        max-width: 600px;
                        margin: 20px auto;
                        background-color: #ffffff;
                        border-radius: 10px;
                        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
                        overflow: hidden;
                    }}
                    .header {{
                        background-color: #FF7043;
                        padding: 20px;
                        text-align: center;
                    }}
                    .header h1 {{
                        color: #ffffff;
                        margin: 0;
                    }}
                    .header img {{
                        max-width: 150px;
                        height: auto;
                    }}
                    .content {{
                        padding: 30px;
                        text-align: center;
                        color: #333;
                    }}
                    .content h2 {{
                        font-size: 24px;
                        margin-bottom: 20px;
                        color: #2d3436;
                    }}
                    .content p {{
                        font-size: 16px;
                        line-height: 1.6;
                        margin-bottom: 20px;
                    }}
                    .btn {{
                        display: inline-block;
                        padding: 12px 24px;
                        background-color: #FF7043;
                        color: #ffffff !important;
                        text-decoration: none;
                        font-size: 16px;
                        font-weight: bold;
                        border-radius: 5px;
                        transition: background-color 0.3s ease;
                    }}
                    .btn:hover {{
                        background-color: #E64A19;
                    }}
                    .footer {{
                        background-color: #f8f8f8;
                        padding: 15px;
                        text-align: center;
                        font-size: 14px;
                        color: #777;
                    }}
                    .footer a {{
                        color: #FF7043;
                        text-decoration: none;
                    }}
                    .footer a:hover {{
                        text-decoration: underline;
                    }}
                </style>
                </head>
                <body>
                    <div class='container'>
                    <!-- Header -->
                    <div class='header'>
                        <h1>Bloomie Shop</h1>
                    </div>
                    <!-- Content -->
                    <div class='content'>
                        <h2>Đặt lại mật khẩu của bạn</h2>
                        <p>Xin chào {checkMail.FullName ?? "Khách hàng thân mến"},</p>
                        <p>Chúng tôi đã nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại Bloomie Flower Shop. Vui lòng nhấn vào nút bên dưới để tiến hành đổi mật khẩu:</p>
                        <a href='{resetLink}' class='btn'>Đổi mật khẩu</a>
                        <p>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này hoặc liên hệ với chúng tôi qua hotline <strong>0987 654 321</strong>.</p>
                    </div>
                    <!-- Footer -->
                    <div class='footer'>
                        <p>© 2025 Bloomie Flower Shop. Tất cả quyền được bảo lưu.</p>
                        <p>Theo dõi chúng tôi:
                        <a href='#'>Facebook</a> |
                        <a href='#'>Instagram</a>
                    </p>
                    <p>Hotline: 0987 654 321 | Email: bloomieshop25@gmail.vn</p>
                    </div>
                    </div>
                </body>
                </html>";

                await _emailService.SendEmailAsync(receiver, subject, message);
            }

            TempData["success"] = "Một email chứa hướng dẫn đặt lại mật khẩu đã được gửi đến địa chỉ email của bạn.";
            return RedirectToAction("ForgotPassword", "Account");
        }

        public async Task<IActionResult> ForgotPassword()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> UpdateAccount()
        {
            // Kiểm tra người dùng đã đăng nhập chưa
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            // Lấy thông tin người dùng từ Claims
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return NotFound();
            }

            // Đảm bảo Token không null
            if (string.IsNullOrEmpty(user.Token))
            {
                user.Token = Guid.NewGuid().ToString();
                await _userManager.UpdateAsync(user);
            }

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAccount(ApplicationUser model, string NewPassword, string ConfirmNewPassword)
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return View(model);
            }

            if (!string.IsNullOrEmpty(model.Email) && model.Email != user.Email)
            {
                // Kiểm tra email đã tồn tại chưa
                var existingEmail = await _userManager.FindByEmailAsync(model.Email);
                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", "Email này đã được sử dụng.");
                    TempData["error"] = "Email này đã được sử dụng.";
                    return View(model);
                }

                // Gửi email xác thực đến email mới
                var token = await _userManager.GenerateChangeEmailTokenAsync(user, model.Email);
                var confirmationLink = Url.Action("ConfirmChangeEmail", "Account", new { userId = user.Id, email = model.Email, token = token }, Request.Scheme);
                await _emailService.SendEmailAsync(model.Email, "Xác nhận thay đổi email Bloomie",
                    $"<p>Vui lòng <a href='{confirmationLink}'>nhấn vào đây</a> để xác nhận thay đổi email cho tài khoản Bloomie.</p>");
                TempData["info"] = "Vui lòng kiểm tra email mới để xác nhận thay đổi.";
                return RedirectToAction("UpdateAccount");
            }
            try
            {
                // Xóa lỗi validation cho NewPassword và ConfirmNewPassword trước khi kiểm tra
                ModelState.Remove("NewPassword");
                ModelState.Remove("ConfirmNewPassword");

                // Kiểm tra ModelState (chỉ áp dụng cho các trường trong model)
                if (!ModelState.IsValid)
                {
                    TempData["error"] = "Dữ liệu không hợp lệ. Vui lòng kiểm tra lại.";
                    return View(model);
                }

                // Cập nhật tên người dùng nếu có thay đổi
                if (!string.IsNullOrEmpty(model.UserName) && model.UserName != user.UserName)
                {
                    var setUserNameResult = await _userManager.SetUserNameAsync(user, model.UserName);
                    if (!setUserNameResult.Succeeded)
                    {
                        foreach (var error in setUserNameResult.Errors)
                        {
                            ModelState.AddModelError("", error.Description);
                        }
                        TempData["error"] = "Cập nhật tên người dùng thất bại.";
                        return View(model);
                    }
                }

                // Cập nhật số điện thoại và họ tên
                if (!string.IsNullOrEmpty(model.PhoneNumber))
                {
                    user.PhoneNumber = model.PhoneNumber;
                }
                if (!string.IsNullOrEmpty(model.FullName))
                {
                    user.FullName = model.FullName;
                }

                // Kiểm tra và cập nhật mật khẩu nếu NewPassword không rỗng
                if (!string.IsNullOrEmpty(NewPassword) || !string.IsNullOrEmpty(ConfirmNewPassword))
                {
                    // Nếu một trong hai trường có giá trị, cả hai phải có giá trị và khớp nhau
                    if (string.IsNullOrEmpty(NewPassword) || string.IsNullOrEmpty(ConfirmNewPassword))
                    {
                        ModelState.AddModelError("", "Vui lòng nhập cả mật khẩu mới và xác nhận mật khẩu.");
                        TempData["error"] = "Vui lòng nhập cả mật khẩu mới và xác nhận mật khẩu.";
                        return View(model);
                    }

                    if (NewPassword != ConfirmNewPassword)
                    {
                        ModelState.AddModelError("", "Mật khẩu mới và xác nhận mật khẩu không khớp.");
                        TempData["error"] = "Mật khẩu mới và xác nhận mật khẩu không khớp.";
                        return View(model);
                    }

                    // Reset mật khẩu
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var result = await _userManager.ResetPasswordAsync(user, token, NewPassword);
                    if (!result.Succeeded)
                    {
                        foreach (var error in result.Errors)
                        {
                            ModelState.AddModelError("", error.Description);
                        }
                        TempData["error"] = "Cập nhật mật khẩu thất bại: " + string.Join(", ", result.Errors.Select(e => e.Description));
                        return View(model);
                    }
                }

                // Cập nhật thông tin người dùng
                var updateResult = await _userManager.UpdateAsync(user);
                if (updateResult.Succeeded)
                {
                    TempData["success"] = "Cập nhật thông tin tài khoản thành công.";
                    return RedirectToAction("UpdateAccount");
                }
                else
                {
                    foreach (var error in updateResult.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    TempData["error"] = "Cập nhật thông tin tài khoản thất bại: " + string.Join(", ", updateResult.Errors.Select(e => e.Description));
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                TempData["error"] = $"Lỗi hệ thống: {ex.Message}";
                return View(model);
            }
        }

        public async Task<IActionResult> ConfirmChangeEmail(string userId, string email, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Login");
            }
            var result = await _userManager.ChangeEmailAsync(user, email, token);
            if (result.Succeeded)
            {
                TempData["success"] = "Thay đổi email thành công!";
            }
            else
            {
                TempData["error"] = "Thay đổi email thất bại.";
            }
            return RedirectToAction("Profile");
        }

        [HttpPost]
        public async Task<IActionResult> ResendEmailConfirmation()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Profile");
            }

            if (await _userManager.IsEmailConfirmedAsync(user))
            {
                TempData["info"] = "Email của bạn đã được xác thực rồi.";
                return RedirectToAction("Profile");
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmationLink = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, token = token }, Request.Scheme);

            await _emailService.SendEmailAsync(user.Email, "Xác thực email Bloomie",
                $"<p>Chào {user.FullName},</p><p>Vui lòng <a href='{confirmationLink}'>nhấn vào đây</a> để xác thực email và kích hoạt tài khoản Bloomie.</p>");

            TempData["success"] = "Email xác thực đã được gửi lại. Vui lòng kiểm tra hộp thư của bạn.";
            return RedirectToAction("Profile");
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Login");
            }

            var roles = await _userManager.GetRolesAsync(user);
            ViewBag.Role = roles.FirstOrDefault();
            ViewBag.TwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            ViewBag.EmailConfirmed = await _userManager.IsEmailConfirmedAsync(user);

            // Lấy lịch sử đăng nhập
            var loginHistories = await _context.LoginHistories
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.LoginTime)
                .ToListAsync();

            // Parse User Agent và tạo token cho mỗi phiên
            var parsedHistories = loginHistories.Select(h =>
            {
                var (browser, os, deviceType) = ParseUserAgent(h.UserAgent);
                return new
                {
                    Id = h.Id,
                    UserId = h.UserId,
                    Device = deviceType,
                    Platform = os,
                    LoginTime = h.LoginTime.ToLocalTime(),
                    IPAddress = h.IPAddress,
                    SessionId = h.SessionId,
                    IsNewDevice = h.IsNewDevice,
                    Browser = h.Browser,
                    Location = h.Location ?? "Không xác định",
                    SecurityToken = h.GenerateSecurityToken()
                };
            }).ToList();

            ViewBag.LoginHistory = parsedHistories.ToList();
            ViewBag.RecentLoginHistory = parsedHistories.Take(3).ToList();

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfileImage(IFormFile ProfileImage)
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return NotFound();
            }

            // Kiểm tra file
            if (ProfileImage != null && ProfileImage.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(ProfileImage.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                {
                    TempData["error"] = "Chỉ cho phép tải lên file ảnh (.jpg, .jpeg, .png, .gif).";
                    return RedirectToAction("Profile");
                }
                if (ProfileImage.Length > 10 * 1024 * 1024)
                {
                    TempData["error"] = "Kích thước ảnh không được vượt quá 10MB.";
                    return RedirectToAction("Profile");
                }

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/profiles");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                if (!string.IsNullOrEmpty(user.ProfileImageUrl) && 
                    user.ProfileImageUrl != "/images/profiles/default-avatar.png")
                {
                    var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.ProfileImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                    }
                }

                var fileName = Guid.NewGuid().ToString() + extension;
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ProfileImage.CopyToAsync(stream);
                }

                user.ProfileImageUrl = $"/images/profiles/{fileName}";
                await _userManager.UpdateAsync(user);
                TempData["success"] = "Cập nhật hình ảnh đại diện thành công.";
            }
            else
            {
                TempData["info"] = "Bạn đã chọn không upload ảnh. Ảnh mặc định sẽ được sử dụng.";
            }

            return RedirectToAction("Profile");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProfileImage()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Profile");
            }

            // Xóa file ảnh cũ nếu không phải ảnh mặc định
            if (!string.IsNullOrEmpty(user.ProfileImageUrl) &&
                user.ProfileImageUrl != "/images/profiles/default-avatar.png")
            {
                var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath,
                    user.ProfileImageUrl.TrimStart('/'));

                if (System.IO.File.Exists(oldImagePath))
                {
                    try
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                    catch (Exception ex)
                    {
                        // Log lỗi nếu cần
                        Console.WriteLine($"Không thể xóa file ảnh: {ex.Message}");
                    }
                }
            }

            // Đặt lại ảnh về mặc định
            user.ProfileImageUrl = "/images/profiles/default-avatar.png";

            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                TempData["success"] = "Đã xóa ảnh đại diện thành công.";
            }
            else
            {
                TempData["error"] = "Có lỗi xảy ra khi xóa ảnh đại diện.";
            }

            return RedirectToAction("Profile");
        }

        [HttpGet]
        public IActionResult DeleteAccount()
        {
            return View();
        }


        [HttpPost]
        public async Task<IActionResult> DeleteAccount(DeleteAccountViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login");
            }

            var passwordCheck = await _userManager.CheckPasswordAsync(user, model.Password);
            if (!passwordCheck)
            {
                ModelState.AddModelError("Password", "Mật khẩu không đúng. Vui lòng nhập lại.");
                TempData["error"] = "Mật khẩu không đúng. Vui lòng nhập lại.";
                return View(model);
            }

            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            user.DeleteReason = model.Reason;
            await _userManager.UpdateAsync(user);

            await _signInManager.SignOutAsync();
            await _emailService.SendEmailAsync(user.Email, "Tài khoản đã bị xóa", "Tài khoản của bạn đã được xóa theo yêu cầu.");

            TempData["success"] = "Tài khoản của bạn đã được xóa (ẩn).";
            return RedirectToAction("Login");
        }

        [HttpPost]
        public async Task<IActionResult> RequestRestore(string email)
        {
            // Giới hạn gửi yêu cầu khôi phục: 1 lần mỗi 10 phút
            string sessionKey = $"RestoreRequest_{email}";
            var lastRequest = HttpContext.Session.GetString(sessionKey);
            if (!string.IsNullOrEmpty(lastRequest) && DateTime.TryParse(lastRequest, out var lastTime))
            {
                if ((DateTime.UtcNow - lastTime).TotalMinutes < 10)
                {
                    TempData["error"] = "Bạn vừa gửi yêu cầu khôi phục. Vui lòng thử lại sau 10 phút.";
                    return RedirectToAction("Login");
                }
            }
            HttpContext.Session.SetString(sessionKey, DateTime.UtcNow.ToString());

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null || !user.IsDeleted)
            {
                TempData["error"] = "Tài khoản không tồn tại hoặc chưa bị xóa.";
                return RedirectToAction("Login");
            }

            if (user.DeletedAt.HasValue && (DateTime.UtcNow - user.DeletedAt.Value).TotalDays > 30)
            {
                TempData["error"] = "Tài khoản đã bị xóa quá lâu và không thể khôi phục.";
                return RedirectToAction("Login");
            }

            // Gửi email tới admin
            await _emailService.SendEmailAsync("bloomieshop25@gmail.com", "Yêu cầu khôi phục tài khoản", $"Người dùng {email} yêu cầu khôi phục tài khoản.");

            TempData["success"] = "Yêu cầu khôi phục đã được gửi. Vui lòng chờ admin xác nhận.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> LogoutDeviceFromEmail(string sessionId, string token)
        {
            // Kiểm tra token bảo mật
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(token))
            {
                TempData["error"] = "Link không hợp lệ.";
                return RedirectToAction("Login");
            }

            // Tìm session trong database
            var session = await _context.LoginHistories
                .FirstOrDefaultAsync(h => h.SessionId == sessionId);

            if (session == null)
            {
                TempData["info"] = "Phiên đăng nhập không tồn tại hoặc đã được đăng xuất.";
                return RedirectToAction("Login");
            }

            // Kiểm tra token (có thể hash sessionId + userId + secret key)
            var expectedToken = session.GenerateSecurityToken();
            if (token != expectedToken)
            {
                TempData["error"] = "Link không hợp lệ hoặc đã hết hạn.";
                return RedirectToAction("Login");
            }

            // Xóa session
            _context.LoginHistories.Remove(session);
            await _context.SaveChangesAsync();

            // Gửi email xác nhận
            var user = await _userManager.FindByIdAsync(session.UserId);
            if (user != null)
            {
                await _emailService.SendEmailAsync(user.Email, "Thiết bị đã được đăng xuất",
                    $"Thiết bị với IP {session.IPAddress} đã được đăng xuất thành công vào {DateTime.UtcNow:HH:mm dd/MM/yyyy}.");
            }

            TempData["success"] = "Thiết bị đã được đăng xuất thành công.";
            return View("DeviceLoggedOut"); // Tạo view riêng để thông báo
        }

        // Helper method lấy thông tin vị trí từ IP
        private async Task<string> GetLocationFromIP(string ip)
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync($"http://ip-api.com/json/{ip}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    if (data.GetProperty("status").GetString() == "success")
                    {
                        var city = data.GetProperty("city").GetString();
                        var country = data.GetProperty("country").GetString();
                        return $"{city}, {country}";
                    }
                }
                return "Không xác định";
            }
            catch
            {
                return "Không xác định";
            }
        }

        // Helper method lấy IP thật của client
        private string GetClientIpAddress()
        {
            // Kiểm tra các header có thể chứa IP thật
            var ipAddress = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ipAddress))
            {
                // X-Forwarded-For có thể chứa nhiều IP, lấy IP đầu tiên
                ipAddress = ipAddress.Split(',')[0].Trim();
                Console.WriteLine($"Using X-Forwarded-For: {ipAddress}");
                return ipAddress;
            }

            ipAddress = Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ipAddress))
            {
                Console.WriteLine($"Using X-Real-IP: {ipAddress}");
                return ipAddress;
            }

            ipAddress = Request.Headers["CF-Connecting-IP"].FirstOrDefault(); // Cloudflare
            if (!string.IsNullOrEmpty(ipAddress))
            {
                Console.WriteLine($"Using CF-Connecting-IP: {ipAddress}");
                return ipAddress;
            }

            ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(ipAddress))
            {
                Console.WriteLine($"Using RemoteIpAddress: {ipAddress}");
                return ipAddress;
            }

            Console.WriteLine("No IP found, returning Unknown");
            return "Unknown";
        }

        // Helper method parse User Agent thành thông tin dễ hiểu
        private (string browser, string os, string deviceType) ParseUserAgent(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
                return ("Không xác định", "Không xác định", "Không xác định");

            var browser = "Không xác định";
            var os = "Không xác định";
            var deviceType = "Desktop";

            // Parse Browser
            if (userAgent.Contains("Chrome") && !userAgent.Contains("Edg"))
                browser = "Google Chrome";
            else if (userAgent.Contains("Firefox"))
                browser = "Mozilla Firefox";
            else if (userAgent.Contains("Safari") && !userAgent.Contains("Chrome"))
                browser = "Safari";
            else if (userAgent.Contains("Edg"))
                browser = "Microsoft Edge";
            else if (userAgent.Contains("Opera"))
                browser = "Opera";

            // Parse OS
            if (userAgent.Contains("Windows NT 10.0"))
                os = "Windows 10/11";
            else if (userAgent.Contains("Windows NT"))
                os = "Windows";
            else if (userAgent.Contains("Mac OS X"))
                os = "macOS";
            else if (userAgent.Contains("Linux"))
                os = "Linux";
            else if (userAgent.Contains("Android"))
                os = "Android";
            else if (userAgent.Contains("iPhone") || userAgent.Contains("iPad"))
                os = "iOS";

            // Parse Device Type
            if (userAgent.Contains("Mobile") || userAgent.Contains("Android"))
                deviceType = "Điện thoại";
            else if (userAgent.Contains("iPad") || userAgent.Contains("Tablet"))
                deviceType = "Máy tính bảng";
            else
                deviceType = "Máy tính";

            return (browser, os, deviceType);
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogoutDevice(string sessionId)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Unauthorized();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            var session = await _context.LoginHistories
                .FirstOrDefaultAsync(h => h.UserId == user.Id && h.SessionId == sessionId);

            if (session == null)
            {
                TempData["error"] = "Không tìm thấy phiên đăng nhập.";
                return RedirectToAction("Profile");
            }

            // Nếu đang đăng xuất phiên hiện tại
            if (session.SessionId == HttpContext.Session.GetString("SessionId"))
            {
                _context.LoginHistories.Remove(session);
                await _context.SaveChangesAsync();
                await _signInManager.SignOutAsync();
                return RedirectToAction("Login");
            }

            // Xóa phiên đăng nhập khác
            _context.LoginHistories.Remove(session);
            await _context.SaveChangesAsync();
            TempData["success"] = "Đã đăng xuất thiết bị thành công.";
            return RedirectToAction("Profile");
        }

        // Helper method kiểm tra thiết bị mới
        private async Task<bool> IsNewDevice(string userId, string userAgent, string ipAddress)
        {
            // Kiểm tra xem đã có lịch sử đăng nhập với UserAgent và IP này chưa
            return !await _context.LoginHistories
                .AnyAsync(h => h.UserId == userId && 
                              h.UserAgent == userAgent && 
                              h.IPAddress == ipAddress);
        }

        [HttpPost]
        public async Task<IActionResult> RequestUnlock(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["error"] = "Vui lòng nhập email.";
                return RedirectToAction("Login");
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy tài khoản.";
                return RedirectToAction("Login");
            }

            // Kiểm tra xem tài khoản có bị khóa không
            if (!await _userManager.IsLockedOutAsync(user))
            {
                TempData["error"] = "Tài khoản không bị khóa.";
                return RedirectToAction("Login");
            }

            // Kiểm tra giới hạn request (1 lần/10 phút)
            var lastRequest = await _context.UnlockRequests
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync();

            if (lastRequest != null && (DateTime.UtcNow - lastRequest.RequestedAt).TotalMinutes < 10)
            {
                TempData["error"] = "Vui lòng đợi 10 phút trước khi gửi yêu cầu mới.";
                return RedirectToAction("Login");
            }

            // Tạo token mới
            var token = Guid.NewGuid().ToString("N");
            user.Token = token;
            await _userManager.UpdateAsync(user);

            // Lưu yêu cầu mở khóa
            var unlockRequest = new UnlockRequest
            {
                UserId = user.Id,
                Token = token,
                RequestedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                Status = "Pending"
            };

            _context.UnlockRequests.Add(unlockRequest);
            await _context.SaveChangesAsync();

            // Kiểm tra nếu user chưa có password (đăng nhập bằng Google)
            var hasPassword = await _userManager.HasPasswordAsync(user);

            var unlockLink = hasPassword
                ? Url.Action("UnlockAccount", "Account", new { email = email, token = token }, Request.Scheme)
                : Url.Action("SetNewPassword", "Account", new { email = email, token = token }, Request.Scheme);

            var emailBody = $@"
                                <h2>Hướng dẫn mở khóa tài khoản</h2>
                                <p>Chào {user.FullName},</p>
                                <p>Chúng tôi đã nhận được yêu cầu mở khóa tài khoản của bạn.</p>
                                {(hasPassword ? "<p>Để mở khóa tài khoản, vui lòng nhấp vào liên kết dưới đây:</p>"
                                            : "<p>Vì tài khoản của bạn sử dụng đăng nhập bằng Google, bạn cần thiết lập mật khẩu mới để tăng cường bảo mật:</p>"
                                )}
                                <p><a href='{unlockLink}'>Nhấp vào đây để {(hasPassword ? "mở khóa tài khoản" : "thiết lập mật khẩu và mở khóa")}</a></p>
                                <p>Liên kết này sẽ hết hạn sau 24 giờ.</p>";

            await _emailService.SendEmailAsync(email, "Hướng dẫn mở khóa tài khoản Bloomie", emailBody);

            TempData["success"] = "Hướng dẫn mở khóa đã được gửi tới email của bạn.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> UnlockAccount(string email, string token)
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == email && u.Token == token);

            if (user == null)
            {
                TempData["error"] = "Liên kết không hợp lệ.";
                return RedirectToAction("Login");
            }

            var request = await _context.UnlockRequests
                .Where(r => r.UserId == user.Id && r.Token == token && r.Status == "Pending")
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync();

            if (request == null || request.ExpiresAt < DateTime.UtcNow)
            {
                TempData["error"] = "Liên kết đã hết hạn. Vui lòng yêu cầu mở khóa lại.";
                return RedirectToAction("Login");
            }

            // Mở khóa tài khoản
            await _userManager.SetLockoutEndDateAsync(user, null);

            // Cập nhật token và security stamp
            user.Token = Guid.NewGuid().ToString();
            await _userManager.UpdateSecurityStampAsync(user);
            await _userManager.UpdateAsync(user);

            // Cập nhật trạng thái request
            request.Status = "Completed";
            request.DecidedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Gửi email thông báo
            await _emailService.SendEmailAsync(user.Email,
                "Tài khoản đã được mở khóa",
                "Tài khoản của bạn đã được mở khóa thành công. Bạn có thể đăng nhập lại.");

            TempData["success"] = "Tài khoản đã được mở khóa thành công. Vui lòng đăng nhập lại.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> SetNewPassword(string email, string token)
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == email && u.Token == token);

            if (user == null)
            {
                TempData["error"] = "Liên kết không hợp lệ hoặc đã hết hạn.";
                return RedirectToAction("Login");
            }

            // Kiểm tra request có còn hiệu lực
            var request = await _context.UnlockRequests
                .Where(r => r.UserId == user.Id && r.Token == token && r.Status == "Pending")
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync();

            if (request == null || request.ExpiresAt < DateTime.UtcNow)
            {
                TempData["error"] = "Liên kết đã hết hạn. Vui lòng yêu cầu mở khóa lại.";
                return RedirectToAction("Login");
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SetNewPassword(string email, string token, string newPassword)
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == email && u.Token == token);

            if (user == null)
            {
                TempData["error"] = "Liên kết không hợp lệ.";
                return RedirectToAction("Login");
            }

            // Kiểm tra và cập nhật trạng thái request
            var request = await _context.UnlockRequests
                .Where(r => r.UserId == user.Id && r.Token == token && r.Status == "Pending")
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync();

            if (request == null || request.ExpiresAt < DateTime.UtcNow)
            {
                TempData["error"] = "Yêu cầu đã hết hạn. Vui lòng thực hiện lại.";
                return RedirectToAction("Login");
            }

            // Reset password và mở khóa
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);

            if (result.Succeeded)
            {
                // Mở khóa tài khoản
                await _userManager.SetLockoutEndDateAsync(user, null);

                // Tạo token mới và cập nhật security stamp
                user.Token = Guid.NewGuid().ToString();
                await _userManager.UpdateSecurityStampAsync(user);
                await _userManager.UpdateAsync(user);

                // Cập nhật trạng thái request
                request.Status = "Completed";
                request.DecidedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Gửi email thông báo
                await _emailService.SendEmailAsync(user.Email,
                    "Tài khoản đã được mở khóa",
                    "Tài khoản của bạn đã được mở khóa thành công. Bạn có thể đăng nhập với mật khẩu mới.");

                TempData["success"] = "Mật khẩu đã được đặt lại và tài khoản đã được mở khóa. Vui lòng đăng nhập.";
                return RedirectToAction("Login");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        // Method tự động tặng voucher chào mừng cho thành viên mới
        private async Task GiveWelcomeVoucherAsync(string userId)
        {
            // Tìm voucher chào mừng (code chứa "WELCOME")
            // Không cần kiểm tra IsPublic vì đây là voucher đặc biệt do hệ thống tự tặng
            var welcomeVoucher = await _context.PromotionCodes
                .Include(pc => pc.Promotion)
                .Where(pc => pc.IsActive 
                    && pc.Code.ToUpper().Contains("WELCOME") 
                    && (!pc.ExpiryDate.HasValue || pc.ExpiryDate.Value > DateTime.Now))
                .FirstOrDefaultAsync();

            if (welcomeVoucher != null)
            {
                // Kiểm tra xem user đã có voucher này chưa
                var existing = await _context.UserVouchers
                    .FirstOrDefaultAsync(uv => uv.UserId == userId && uv.PromotionCodeId == welcomeVoucher.Id);
                
                if (existing != null)
                {
                    return;
                }
                
                // Thêm voucher vào tài khoản user
                var userVoucher = new UserVoucher
                {
                    UserId = userId,
                    PromotionCodeId = welcomeVoucher.Id,
                    CollectedDate = DateTime.Now,
                    ExpiryDate = welcomeVoucher.ExpiryDate ?? DateTime.Now.AddMonths(3),
                    Source = "WelcomeGift",
                    Note = "Voucher chào mừng thành viên mới",
                    IsUsed = false
                };

                _context.UserVouchers.Add(userVoucher);
                await _context.SaveChangesAsync();
            }
        }
    }
}
