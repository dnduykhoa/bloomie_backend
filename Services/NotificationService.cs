using Microsoft.AspNetCore.SignalR;
using Bloomie.Hubs;
using Bloomie.Data;
using Bloomie.Models.Entities;

namespace Bloomie.Services
{
    public interface INotificationService
    {
        Task SendNotificationToAdmins(string message, string? link = null, string type = "info");
    }

    public class NotificationService : INotificationService
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ApplicationDbContext _context;

        public NotificationService(IHubContext<NotificationHub> hubContext, ApplicationDbContext context)
        {
            _hubContext = hubContext;
            _context = context;
        }

        public async Task SendNotificationToAdmins(string message, string? link = null, string type = "info")
        {
            // 1. Lưu vào database
            var dbNotification = new Notification
            {
                Message = message,
                Link = link,
                Type = type,
                IsRead = false,
                CreatedAt = DateTime.Now,
                UserId = null // null = thông báo cho tất cả admin
            };

            _context.Notifications.Add(dbNotification);
            await _context.SaveChangesAsync();

            // 2. Gửi realtime qua SignalR
            var notification = new
            {
                id = dbNotification.Id,
                message = dbNotification.Message,
                link = dbNotification.Link ?? "#",
                type = dbNotification.Type,
                createdAt = dbNotification.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                isRead = dbNotification.IsRead
            };

            await _hubContext.Clients.Group("AdminGroup").SendAsync("ReceiveNotification", notification);
        }
    }
}
