using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.ApiRequests;
using Bloomie.Services.Interfaces;
using Bloomie.Models.ViewModels;
using Google.Apis.Auth;

namespace Bloomie.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountApiController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AccountApiController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext context, IEmailService emailService, IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _context = context;
            _emailService = emailService;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginViewModel loginVM)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            var user = await _userManager.FindByNameAsync(loginVM.UserName);
            if (user == null)
            {
                return Unauthorized(new { field = "UserName", message = "Tên đăng nhập không đúng." });
            }
            if (user.IsDeleted)
            {
                return Unauthorized(new { field = "UserName", message = "Tài khoản đã bị xóa." });
            }
            // Kiểm tra nếu đã bị khóa
            if (await _userManager.IsLockedOutAsync(user))
            {
                return Unauthorized(new { field = "UserName", message = "Tài khoản đã bị khóa do nhập sai quá số lần cho phép." });
            }
            var isPasswordValid = await _userManager.CheckPasswordAsync(user, loginVM.Password);
            if (!isPasswordValid)
            {
                // Tăng số lần đăng nhập sai
                await _userManager.AccessFailedAsync(user);
                var accessFailedCount = await _userManager.GetAccessFailedCountAsync(user);
                var maxFailed = _userManager.Options.Lockout.MaxFailedAccessAttempts;
                var remain = maxFailed - accessFailedCount;
                string msg = remain > 0
                    ? $"Mật khẩu không đúng. Bạn còn {remain} lần thử trước khi tài khoản bị khóa."
                    : "Mật khẩu không đúng. Tài khoản đã bị khóa do nhập sai quá số lần cho phép.";
                return Unauthorized(new { field = "Password", message = msg });
            }
            // Reset lại số lần sai nếu đúng mật khẩu
            await _userManager.ResetAccessFailedCountAsync(user);

            // Đăng nhập cho web: dùng SignInManager (cookie/session)
            var result = await _signInManager.PasswordSignInAsync(loginVM.UserName, loginVM.Password, loginVM.RememberMe, lockoutOnFailure: true);

            // Đăng nhập cho app/mobile: kiểm tra trạng thái 2FA thủ công
            var isTwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            if (isTwoFactorEnabled)
            {
                // Nếu bật 2FA, gửi mã xác thực và trả về requiresTwoFactor cho app
                var token = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
                await _emailService.SendEmailAsync(user.Email, "Mã xác thực hai bước", $"Mã xác thực hai bước của bạn là: <strong>{token}</strong>.");
                return Ok(new { requiresTwoFactor = true, userName = user.UserName });
            }
            // Nếu không bật 2FA, xử lý như bình thường
            if (result.Succeeded)
            {
                try
                {
                    // Lấy role của user
                    var roles = await _userManager.GetRolesAsync(user);
                    var role = roles.FirstOrDefault() ?? "User";

                    // Kiểm tra xem device này đã đăng nhập bao giờ chưa
                    var deviceKey = $"{loginVM.DeviceName}_{loginVM.ClientIPAddress}";
                    var existingDevice = await _context.LoginHistories
                        .AnyAsync(h => h.UserId == user.Id && 
                                      h.UserAgent == loginVM.DeviceName && 
                                      h.IPAddress == loginVM.ClientIPAddress);
                    
                    var loginHistory = new Bloomie.Models.Entities.LoginHistory
                    {
                        UserId = user.Id,
                        LoginTime = DateTime.UtcNow,
                        IPAddress = loginVM.ClientIPAddress ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                        UserAgent = loginVM.DeviceName ?? Request.Headers["User-Agent"].ToString(),
                        IsNewDevice = !existingDevice, // Chỉ là new nếu chưa từng đăng nhập
                        SessionId = Guid.NewGuid().ToString(),
                        Browser = "Mobile App",
                        Location = "Unknown"
                    };
                    _context.LoginHistories.Add(loginHistory);
                    await _context.SaveChangesAsync();
                    return Ok(new { success = true, message = "Đăng nhập thành công", role = role, userName = user.UserName });
                }
                catch (Exception ex)
                {
                    // Nếu lỗi khi ghi lịch sử, vẫn cho đăng nhập nhưng trả về thông báo lỗi phụ
                    return Ok(new { success = true, message = "Đăng nhập thành công, nhưng không ghi được lịch sử đăng nhập.", error = ex.Message });
                }
            }
            else if (result.IsLockedOut)
            {
                return Unauthorized(new { field = "UserName", message = "Tài khoản đã bị khóa do nhập sai quá số lần cho phép." });
            }
            // Trường hợp khác (hiếm gặp)
            return Unauthorized(new { message = "Đăng nhập không thành công." });
        }

        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // Verify Google ID Token với Google API
                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { 
                        "617139140468-2dbsp0lihg750nrgma1h9kduqulbuigs.apps.googleusercontent.com",  // Web Client ID
                        "617139140468-k3nle4jd0o315s3d28hts19sahiajvrc.apps.googleusercontent.com",  // iOS Client ID  
                        "617139140468-gjpdic0j2t90tvlf592ongjrhth5cjih.apps.googleusercontent.com"   // Android Client ID
                     }
                };

                GoogleJsonWebSignature.Payload payload;
                try
                {
                    payload = await GoogleJsonWebSignature.ValidateAsync(model.IdToken, settings);
                }
                catch (Exception ex)
                {
                    return Unauthorized(new { message = "Google ID Token không hợp lệ.", error = ex.Message });
                }

                // Kiểm tra email từ token có khớp với email gửi lên không
                if (payload.Email != model.Email)
                {
                    return Unauthorized(new { message = "Email không khớp với Google ID Token." });
                }

                // Tìm hoặc tạo user
                var user = await _userManager.FindByEmailAsync(payload.Email);
                
                if (user == null)
                {
                    // Tạo user mới từ Google
                    var userRole = await _roleManager.FindByNameAsync("User");
                    if (userRole == null)
                        return BadRequest(new { message = "Không tìm thấy role 'User'. Hãy tạo role này trước." });

                    user = new ApplicationUser
                    {
                        UserName = payload.Email.Split('@')[0],
                        Email = payload.Email,
                        FullName = payload.Name ?? payload.Email.Split('@')[0],
                        RoleId = userRole.Id,
                        Token = Guid.NewGuid().ToString(),
                        EmailConfirmed = true // Google đã xác thực email rồi
                    };

                    var result = await _userManager.CreateAsync(user);
                    if (!result.Succeeded)
                        return BadRequest(result.Errors);

                    await _userManager.AddToRoleAsync(user, "User");
                }

                // Kiểm tra tài khoản đã bị xóa
                if (user.IsDeleted)
                {
                    return Unauthorized(new { message = "Tài khoản đã bị xóa." });
                }

                // Tạo authentication cookie
                await _signInManager.SignInAsync(user, isPersistent: true);

                // Ghi lịch sử đăng nhập
                try
                {
                    // Kiểm tra device đã đăng nhập chưa
                    var existingDevice = await _context.LoginHistories
                        .AnyAsync(h => h.UserId == user.Id && 
                                      h.UserAgent == model.DeviceName && 
                                      h.IPAddress == model.ClientIPAddress);
                    
                    var loginHistory = new Bloomie.Models.Entities.LoginHistory
                    {
                        UserId = user.Id,
                        LoginTime = DateTime.UtcNow,
                        IPAddress = model.ClientIPAddress ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                        UserAgent = model.DeviceName ?? Request.Headers["User-Agent"].ToString(),
                        IsNewDevice = !existingDevice, // Chỉ là new nếu chưa từng đăng nhập
                        SessionId = Guid.NewGuid().ToString(),
                        Browser = "Mobile App",
                        Location = "Unknown"
                    };
                    _context.LoginHistories.Add(loginHistory);
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Nếu lỗi khi ghi lịch sử, vẫn cho đăng nhập
                    return Ok(new { success = true, message = "Đăng nhập Google thành công, nhưng không ghi được lịch sử đăng nhập.", error = ex.Message });
                }

            return Ok(new { 
                success = true, 
                message = "Đăng nhập Google thành công",
                userName = user.UserName,
                email = user.Email,
                fullName = user.FullName
            });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi xác thực Google.", error = ex.Message });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserViewModel userVM)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            var existingUser = await _userManager.FindByNameAsync(userVM.UserName);
            if (existingUser != null)
                return BadRequest(new { message = "Tên đăng nhập đã tồn tại." });
            // Lấy role 'User' từ RoleManager
            var userRole = await _roleManager.FindByNameAsync("User");
            if (userRole == null)
                return BadRequest(new { message = "Không tìm thấy role 'User'. Hãy tạo role này trước." });
            var newUser = new ApplicationUser
            {
                FullName = userVM.FullName,
                UserName = userVM.UserName,
                Email = userVM.Email,
                RoleId = userRole.Id,
                Token = Guid.NewGuid().ToString()
            };
            var result = await _userManager.CreateAsync(newUser, userVM.Password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(newUser, "User");
                // Gửi email xác nhận đăng ký
                var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(newUser);
                var confirmLink = $"{Request.Scheme}://{Request.Host}/Account/ConfirmEmail?userId={newUser.Id}&token={Uri.EscapeDataString(emailToken)}";
                string emailContent = $@"
                    <p>Xin chào <strong>{newUser.FullName}</strong>,</p>
                    <p>Bạn vừa đăng ký tài khoản Bloomie thành công.</p>
                    <p>Vui lòng nhấn vào liên kết dưới đây để xác thực email:</p>
                    <p><a href='{confirmLink}'>{confirmLink}</a></p>
                    <p>Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email này.</p>
                    <p>Trân trọng,<br/>Bloomie Team</p>";
                await _emailService.SendEmailAsync(newUser.Email, "Xác thực đăng ký Bloomie", emailContent);
                // Đăng nhập luôn sau khi đăng ký thành công
                await _signInManager.SignInAsync(newUser, isPersistent: false);
                return Ok(new { success = true, message = "Đăng ký và đăng nhập thành công! Email xác nhận đã được gửi." });
            }
            return BadRequest(result.Errors);
        }

        [HttpPost("enable-2fa")]
        public async Task<IActionResult> EnableTwoFactor([FromBody] EnableTwoFactorRequest req)
        {
            var user = await _userManager.FindByNameAsync(req.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var currentStatus = await _userManager.GetTwoFactorEnabledAsync(user);
            if (req.Enable != currentStatus)
            {
                if (req.Enable)
                {
                    var token = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
                    await _emailService.SendEmailAsync(user.Email, "Xác thực hai bước", $"Mã xác thực hai bước của bạn là: <strong>{token}</strong>.");
                    return Ok(new { message = "Mã xác thực đã được gửi đến email." });
                }
                else
                {
                    await _userManager.SetTwoFactorEnabledAsync(user, false);
                    return Ok(new { message = "Đã tắt xác thực hai bước." });
                }
            }
            return Ok(new { message = $"Xác thực hai bước đã {(req.Enable ? "được bật" : "bị tắt")} từ trước." });
        }

        [HttpPost("verify-2fa")]
        public async Task<IActionResult> VerifyTwoFactor([FromBody] TwoFactorViewModel model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return Unauthorized(new { message = "Không tìm thấy người dùng." });
            var result = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, model.TwoFactorCode);
            if (result)
            {
                await _userManager.SetTwoFactorEnabledAsync(user, true);
                return Ok(new { success = true, message = "Xác thực hai bước đã được bật thành công." });
            }
            return Unauthorized(new { message = "Mã xác thực không đúng." });
        }

        [HttpPost("resend-2fa-code")]
        public async Task<IActionResult> ResendTwoFactorCode([FromBody] ResendTwoFactorRequest req)
        {
            var user = await _userManager.FindByNameAsync(req.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var token = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
            await _emailService.SendEmailAsync(user.Email, "Mã xác thực hai bước mới", $"Mã xác thực hai bước mới của bạn là: <strong>{token}</strong>.");
            return Ok(new { message = "Mã xác thực mới đã được gửi lại." });
        }

        [HttpGet("profile/{username}")]
        public async Task<IActionResult> GetProfile(string username)
        {
            var user = await _userManager.FindByNameAsync(username);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });

            // Lấy role, 2FA, xác thực email
            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault();
            var twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            var emailConfirmed = await _userManager.IsEmailConfirmedAsync(user);

            // Lấy lịch sử đăng nhập
            var loginHistories = await _context.LoginHistories
                .Where(h => h.UserId == user.Id)
                .OrderByDescending(h => h.LoginTime)
                .ToListAsync();

            // Tạo response cho login history đơn giản
            var parsedHistories = loginHistories.Select(h => new {
                h.Id,
                h.UserId,
                Device = h.UserAgent, // UserAgent giờ chứa device name từ client
                LoginTime = h.LoginTime,
                IPAddress = h.IPAddress,
                h.SessionId,
                h.IsNewDevice
            }).ToList();

            return Ok(new {
                user.UserName,
                user.FullName,
                user.Email,
                user.PhoneNumber,
                user.ProfileImageUrl,
                Role = role,
                TwoFactorEnabled = twoFactorEnabled,
                EmailConfirmed = emailConfirmed,
                LoginHistory = parsedHistories,
                RecentLoginHistory = parsedHistories.Take(3).ToList()
            });
        }

        [HttpPost("two-factor-login")]
        public async Task<IActionResult> TwoFactorLogin([FromBody] TwoFactorViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, model.TwoFactorCode);
            if (isValid)
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
                return Ok(new { success = true, message = "Đăng nhập thành công" });
            }
            return Unauthorized(new { message = "Mã xác thực không đúng." });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return Ok(new { success = true, message = "Đăng xuất thành công" });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
            if (result.Succeeded)
                return Ok(new { success = true, message = "Đổi mật khẩu thành công" });
            return BadRequest(result.Errors);
        }

        // Giữ lại các hàm OTP mới nhất bên dưới
        [HttpPost("send-mail-forgot-pass")]
        public async Task<IActionResult> SendMailForgotPass([FromBody] ForgotPasswordRequest model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null || user.Email != model.Email)
                return BadRequest(new { message = "Thông tin chưa chính xác." });
            // Sinh mã OTP 6 số
            var rng = new Random();
            var otp = rng.Next(100000, 999999).ToString();
            user.Token = otp;
            user.TokenCreatedAt = DateTime.UtcNow;
            _context.Update(user);
            await _context.SaveChangesAsync();
            string emailContent = $@"
                <p>Xin chào <strong>{user.FullName}</strong>,</p>
                <p>Bạn vừa yêu cầu đặt lại mật khẩu cho tài khoản Bloomie.</p>
                <p>Mã xác thực đặt lại mật khẩu của bạn là: <strong>{otp}</strong></p>
                <p>Mã này có hiệu lực trong 10 phút.</p>
                <p>Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email này.</p>
                <p>Trân trọng,<br/>Bloomie Shop</p>";
            await _emailService.SendEmailAsync(user.Email, "Mã xác thực đặt lại mật khẩu Bloomie", emailContent);
            return Ok(new { message = "Mã xác thực đã được gửi đến email của bạn." });
        }

        [HttpPost("verify-reset-otp")]
        public async Task<IActionResult> VerifyResetOtp([FromBody] VerifyOtpRequest model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return Unauthorized(new { message = "Email không tồn tại." });
            if (string.IsNullOrEmpty(user.Token) || user.Token != model.Otp)
                return Unauthorized(new { message = "Mã xác thực không đúng." });
            if (user.TokenCreatedAt == null || (DateTime.UtcNow - user.TokenCreatedAt.Value).TotalMinutes > 10)
                return Unauthorized(new { message = "Mã xác thực đã hết hạn." });
            return Ok(new { success = true, message = "Xác thực thành công. Bạn có thể đặt lại mật khẩu." });
        }

        [HttpPost("new-password")]
        public async Task<IActionResult> NewPassword([FromBody] NewPasswordRequest model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return NotFound(new { message = "Email không tồn tại." });
            // Kiểm tra OTP
            if (string.IsNullOrEmpty(user.Token) || user.Token != model.Token)
                return Unauthorized(new { message = "Mã xác thực không đúng." });
            if (user.TokenCreatedAt == null || (DateTime.UtcNow - user.TokenCreatedAt.Value).TotalMinutes > 10)
                return Unauthorized(new { message = "Mã xác thực đã hết hạn." });
            var passwordHasher = new PasswordHasher<ApplicationUser>();
            user.PasswordHash = passwordHasher.HashPassword(user, model.NewPassword);
            user.Token = string.Empty;
            user.TokenCreatedAt = null;
            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
                return Ok(new { success = true, message = "Đặt mật khẩu thành công!" });
            return BadRequest(result.Errors);
        }

        [HttpPost("update-profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });

            // Nếu có yêu cầu đổi tên đăng nhập
            if (!string.IsNullOrEmpty(model.NewUserName) && model.NewUserName != user.UserName)
            {
                var existingUser = await _userManager.FindByNameAsync(model.NewUserName);
                if (existingUser != null)
                {
                    return BadRequest(new { message = "Tên đăng nhập mới đã tồn tại." });
                }
                user.UserName = model.NewUserName;
                // Sau khi đổi UserName, cần reload lại user từ DB theo NewUserName
                await _userManager.UpdateAsync(user);
                user = await _userManager.FindByNameAsync(model.NewUserName);
            }

            user.FullName = model.FullName;
            user.Email = model.Email;
            if (!string.IsNullOrEmpty(model.PhoneNumber))
            {
                user.PhoneNumber = model.PhoneNumber;
            }
            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
                return Ok(new { success = true, message = "Cập nhật thành công" });
            return BadRequest(result.Errors);
        }

        [HttpPost("delete-account")]
        public async Task<IActionResult> DeleteAccount([FromBody] Bloomie.Models.ApiRequests.DeleteAccountViewModel model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            // Kiểm tra mật khẩu
            var passwordCheck = await _userManager.CheckPasswordAsync(user, model.Password);
            if (!passwordCheck)
                return Unauthorized(new { message = "Mật khẩu không đúng." });
            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            await _signInManager.SignOutAsync();
            return Ok(new { success = true, message = "Tài khoản đã bị xóa tạm thời." });
        }

        [HttpPost("request-restore")]
        public async Task<IActionResult> RequestRestore([FromBody] RestoreRequest model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !user.IsDeleted)
                return NotFound(new { message = "Không tìm thấy tài khoản đã xóa." });
            // Gửi email khôi phục
            var restoreToken = Guid.NewGuid().ToString();
            user.Token = restoreToken;
            _context.Update(user);
            await _context.SaveChangesAsync();
            var restoreLink = $"{Request.Scheme}://{Request.Host}/Account/RestoreAccount?email={user.Email}&token={restoreToken}";
            string emailContent = $@"
                <p>Xin chào <strong>{user.FullName}</strong>,</p>
                <p>Bạn vừa yêu cầu khôi phục tài khoản Bloomie đã bị xóa.</p>
                <p>Vui lòng nhấn vào liên kết dưới đây để khôi phục tài khoản:</p>
                <p><a href='{restoreLink}'>{restoreLink}</a></p>
                <p>Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email này.</p>
                <p>Trân trọng,<br/>Bloomie Team</p>";
            await _emailService.SendEmailAsync(user.Email, "Khôi phục tài khoản Bloomie", emailContent);
            return Ok(new { message = "Yêu cầu khôi phục đã được gửi." });
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
                return Ok(new { success = true, message = "Xác thực email thành công." });
            return BadRequest(result.Errors);
        }

        [HttpGet("confirm-change-email")]
        public async Task<IActionResult> ConfirmChangeEmail(string userId, string email, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            // ...logic xác thực đổi email...
            return Ok(new { message = "Xác thực đổi email thành công." });
        }

        [HttpGet("login-history/{userName}")]
        public async Task<IActionResult> LoginHistory(string userName)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var sessions = await _context.LoginHistories.Where(h => h.UserId == user.Id).OrderByDescending(h => h.LoginTime).ToListAsync();
            return Ok(sessions);
        }

        [HttpPost("logout-session")]
        public async Task<IActionResult> LogoutSession([FromBody] LogoutSessionRequest model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var session = await _context.LoginHistories.FirstOrDefaultAsync(h => h.SessionId == model.SessionId && h.UserId == user.Id);
            if (session != null)
            {
                _context.LoginHistories.Remove(session);
                await _context.SaveChangesAsync();
                return Ok(new { message = "Đã đăng xuất khỏi thiết bị thành công." });
            }
            return NotFound(new { message = "Không tìm thấy phiên đăng nhập." });
        }

        [HttpPost("logout-all-sessions")]
        public async Task<IActionResult> LogoutAllSessions([FromBody] LogoutAllSessionsRequest model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var sessions = await _context.LoginHistories.Where(h => h.UserId == user.Id).ToListAsync();
            _context.LoginHistories.RemoveRange(sessions);
            await _context.SaveChangesAsync();
            await _signInManager.SignOutAsync();
            return Ok(new { message = "Đăng xuất khỏi tất cả thiết bị thành công." });
        }

        [HttpPost("update-profile-image")]
        public async Task<IActionResult> UpdateProfileImage([FromForm] UpdateProfileImageRequest model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            var ProfileImage = model.ProfileImage;
            if (ProfileImage != null && ProfileImage.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var extension = Path.GetExtension(ProfileImage.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                    return BadRequest(new { message = "Định dạng ảnh không hợp lệ." });
                if (ProfileImage.Length > 5 * 1024 * 1024)
                    return BadRequest(new { message = "Kích thước ảnh vượt quá 5MB." });
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath,  "images", "profiles");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);
                if (!string.IsNullOrEmpty(user.ProfileImageUrl))
                {
                    var oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath, user.ProfileImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                        System.IO.File.Delete(oldFilePath);
                }
                var fileName = Guid.NewGuid().ToString() + extension;
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ProfileImage.CopyToAsync(stream);
                }
                user.ProfileImageUrl = $"/images/profiles/{fileName}";
                await _userManager.UpdateAsync(user);
                return Ok(new { success = true, message = "Cập nhật ảnh đại diện thành công", imageUrl = user.ProfileImageUrl });
            }
            return BadRequest(new { message = "Không có ảnh tải lên." });
        }

        [HttpPost("delete-profile-image")]
        public async Task<IActionResult> DeleteProfileImage([FromBody] DeleteProfileImageRequest model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            if (!string.IsNullOrEmpty(user.ProfileImageUrl) && user.ProfileImageUrl != "/profiles/default-avatar.png")
            {
                var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, user.ProfileImageUrl.TrimStart('/'));
                if (System.IO.File.Exists(oldImagePath))
                    System.IO.File.Delete(oldImagePath);
                user.ProfileImageUrl = "/profiles/default-avatar.png";
                var result = await _userManager.UpdateAsync(user);
                if (result.Succeeded)
                    return Ok(new { success = true, message = "Xóa ảnh đại diện thành công." });
                else
                    return BadRequest(result.Errors);
            }
            return BadRequest(new { message = "Không có ảnh đại diện để xóa." });
        }

        [HttpGet("logout-device-from-email")]
        public async Task<IActionResult> LogoutDeviceFromEmail(string sessionId, string token)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(token))
                return BadRequest(new { message = "Thiếu thông tin xác thực." });
            var session = await _context.LoginHistories.FirstOrDefaultAsync(h => h.SessionId == sessionId);
            if (session == null)
                return NotFound(new { message = "Không tìm thấy phiên đăng nhập." });
            var user = await _userManager.FindByIdAsync(session.UserId);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });
            // Kiểm tra token hợp lệ
            // ...
            _context.LoginHistories.Remove(session);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã đăng xuất thiết bị thành công." });
        }



        [HttpGet("parse-user-agent")]
        public IActionResult ParseUserAgent([FromQuery] string userAgent)
        {
            // Logic parse user agent (giả lập)
            string browser = "Unknown", os = "Unknown", deviceType = "Unknown";
            if (!string.IsNullOrEmpty(userAgent))
            {
                if (userAgent.Contains("Chrome")) browser = "Chrome";
                else if (userAgent.Contains("Firefox")) browser = "Firefox";
                else if (userAgent.Contains("Safari")) browser = "Safari";
                if (userAgent.Contains("Windows")) os = "Windows";
                else if (userAgent.Contains("Mac")) os = "MacOS";
                else if (userAgent.Contains("Linux")) os = "Linux";
                if (userAgent.Contains("Mobile")) deviceType = "Mobile";
                else deviceType = "Desktop";
            }
            return Ok(new { browser, os, deviceType });
        }

        [HttpGet("is-new-device")]
        public async Task<IActionResult> IsNewDevice([FromQuery] string userId, [FromQuery] string userAgent, [FromQuery] string ip)
        {
            var isNew = !await _context.LoginHistories.AnyAsync(h => h.UserId == userId && h.UserAgent == userAgent && h.IPAddress == ip);
            return Ok(new { isNewDevice = isNew });
        }

        [HttpGet("external-login-url")]
        public IActionResult GetExternalLoginUrl([FromQuery] string provider, [FromQuery] string returnUrl)
        {
            // Tạo URL callback cho provider
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "AccountApi", new { provider, returnUrl }, Request.Scheme);
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            // Lấy URL chuyển hướng thực tế
            var authUrl = properties.RedirectUri;
            if (string.IsNullOrEmpty(authUrl))
            {
                return BadRequest(new { message = "Không tạo được URL đăng nhập ngoài." });
            }
            // Nếu RedirectUri không phải là URL đầy đủ, cần build lại
            if (!authUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // Build absolute URL
                var request = HttpContext.Request;
                var host = request.Scheme + "://" + request.Host;
                authUrl = host + authUrl;
            }
            return Ok(new { url = authUrl });
        }

        [HttpGet("external-login-url-redirect")]
        public IActionResult ExternalLoginRedirect([FromQuery] string provider, [FromQuery] string returnUrl)
        {
            // Sử dụng ChallengeResult để redirect user đến trang xác thực Google/Facebook
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "AccountApi", new { provider, returnUrl }, Request.Scheme);
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return Challenge(properties, provider);
        }

        [HttpGet("external-login-callback")]
        public async Task<IActionResult> ExternalLoginCallback([FromQuery] string provider, [FromQuery] string? returnUrl = null, [FromQuery] string? remoteError = null)
        {
            if (remoteError != null)
                return BadRequest(new { message = $"Lỗi xác thực: {remoteError}" });
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
                return BadRequest(new { message = "Không lấy được thông tin đăng nhập ngoài." });

            // Đăng nhập nếu đã có user
            var signInResult = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false);
            if (signInResult.Succeeded)
            {
                var existingUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                // TODO: Trả về JWT token nếu dùng cho app/mobile
                return Ok(new { success = true, message = "Đăng nhập thành công", user = existingUser });
            }
            // Nếu chưa có user, tạo mới
            var email = info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.Email);
            var userName = email?.Split('@')[0] ?? info.ProviderKey;
            var newUser = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                FullName = userName,
                RoleId = "", // Gán sau nếu cần
                Token = Guid.NewGuid().ToString()
            };
            var result = await _userManager.CreateAsync(newUser);
            if (result.Succeeded)
            {
                await _userManager.AddLoginAsync(newUser, info);
                await _userManager.AddToRoleAsync(newUser, "User");
                // TODO: Trả về JWT token nếu dùng cho app/mobile
                return Ok(new { success = true, message = "Tạo tài khoản và đăng nhập thành công", user = newUser });
            }
            return BadRequest(result.Errors);
        }
    }
}