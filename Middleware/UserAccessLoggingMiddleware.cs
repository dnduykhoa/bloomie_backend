using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bloomie.Data;
using Bloomie.Models.Entities;

namespace Bloomie.Middleware
{
    // Middleware ghi log truy cập của người dùng đã đăng nhập khi vào trang chủ
    public class UserAccessLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UserAccessLoggingMiddleware> _logger;

        public UserAccessLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<UserAccessLoggingMiddleware> logger)
        {
            _next = next;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            _logger.LogInformation("Middleware được gọi để yêu cầu: {Path}", context.Request.Path);

            // Kiểm tra xem người dùng đã đăng nhập chưa
            if (context.User.Identity.IsAuthenticated)
            {
                // Kiểm tra xem request có phải đến trang chủ không
                var path = context.Request.Path.Value?.ToLower();
                if (path == "/" || path == "/home/index")
                {
                    var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        _logger.LogInformation("Người dùng đã xác thực: {UserId} đang truy cập trang chủ", userId);
                        try
                        {
                            // Tạo scope để sử dụng DbContext
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                var log = new UserAccessLog
                                {
                                    UserId = userId,
                                    AccessTime = DateTime.Now,
                                    Url = context.Request.Path // Lưu URL của request
                                };
                                dbContext.UserAccessLogs.Add(log);
                                await dbContext.SaveChangesAsync();
                                _logger.LogInformation("Đã đăng nhập quyền truy cập cho người dùng: {UserId} đến {Url}", userId, log.Url);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "\r\nKhông thể ghi lại quyền truy cập cho người dùng: {UserId}", userId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("\r\nNgười dùng đã được xác thực nhưng không tìm thấy ID người dùng.");
                    }
                }
                else
                {
                    _logger.LogInformation("Người dùng đã xác thực truy cập vào trang không phải trang chủ: {Path}", context.Request.Path);
                }
            }
            else
            {
                _logger.LogInformation("\r\nNgười dùng chưa được xác thực để yêu cầu: {Path}", context.Request.Path);
            }

            await _next(context);
        }
    }
}

