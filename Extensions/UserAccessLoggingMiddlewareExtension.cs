using Microsoft.AspNetCore.Builder;

namespace Bloomie.Middleware
{
    // Phương thức để đăng ký middleware
    public static class UserAccessLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseUserAccessLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<UserAccessLoggingMiddleware>();
        }
    }
}