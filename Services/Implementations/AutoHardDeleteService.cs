using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;

public class AutoHardDeleteService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _interval = TimeSpan.FromHours(12); // Kiểm tra mỗi 12h

    public AutoHardDeleteService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var now = DateTime.UtcNow;

                // 1. Xóa tài khoản cũ (sau 30 ngày)
                var users = userManager.Users
                    .Where(u => u.IsDeleted && u.DeletedAt != null && u.DeletedAt <= now.AddDays(-30))
                    .ToList();

                foreach (var user in users)
                {
                    await userManager.DeleteAsync(user);
                }

                // 2. Xóa phiên đăng nhập cũ (sau 30 ngày)
                var cutoffDate = now.AddDays(-30);
                var oldSessions = await context.LoginHistories
                    .Where(h => h.LoginTime < cutoffDate)
                    .ToListAsync();

                if (oldSessions.Any())
                {
                    context.LoginHistories.RemoveRange(oldSessions);
                    await context.SaveChangesAsync();
                }
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }
}