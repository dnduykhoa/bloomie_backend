using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;

namespace Bloomie.ApiControllers
{
    [Authorize(Roles = "Admin,Manager")]
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public NotificationApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/NotificationApi/GetNotifications?take=10
        [HttpGet("GetNotifications")]
        public async Task<IActionResult> GetNotifications([FromQuery] int take = 20)
        {
            try
            {
                var notifications = await _context.Notifications
                    .Where(n => n.UserId == null) // Chỉ lấy thông báo admin (UserId = null)
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(take)
                    .Select(n => new
                    {
                        id = n.Id,
                        message = n.Message,
                        link = n.Link ?? "#",
                        type = n.Type,
                        createdAt = n.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                        isRead = n.IsRead
                    })
                    .ToListAsync();

                var unreadCount = await _context.Notifications
                    .Where(n => n.UserId == null && !n.IsRead)
                    .CountAsync();

                return Ok(new
                {
                    success = true,
                    data = notifications,
                    unreadCount = unreadCount
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // POST: api/NotificationApi/MarkAsRead/5
        [HttpPost("MarkAsRead/{id}")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            try
            {
                var notification = await _context.Notifications.FindAsync(id);
                if (notification == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy thông báo" });
                }

                notification.IsRead = true;
                await _context.SaveChangesAsync();

                var unreadCount = await _context.Notifications
                    .Where(n => n.UserId == null && !n.IsRead)
                    .CountAsync();

                return Ok(new { success = true, unreadCount = unreadCount });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // POST: api/NotificationApi/MarkAllAsRead
        [HttpPost("MarkAllAsRead")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            try
            {
                var notifications = await _context.Notifications
                    .Where(n => n.UserId == null && !n.IsRead)
                    .ToListAsync();

                foreach (var notification in notifications)
                {
                    notification.IsRead = true;
                }

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Đã đánh dấu tất cả là đã đọc" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // DELETE: api/NotificationApi/Delete/5
        [HttpDelete("Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var notification = await _context.Notifications.FindAsync(id);
                if (notification == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy thông báo" });
                }

                _context.Notifications.Remove(notification);
                await _context.SaveChangesAsync();

                var unreadCount = await _context.Notifications
                    .Where(n => n.UserId == null && !n.IsRead)
                    .CountAsync();

                return Ok(new { success = true, unreadCount = unreadCount });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // DELETE: api/NotificationApi/DeleteAll
        [HttpDelete("DeleteAll")]
        public async Task<IActionResult> DeleteAll()
        {
            try
            {
                var notifications = await _context.Notifications
                    .Where(n => n.UserId == null && n.IsRead)
                    .ToListAsync();

                _context.Notifications.RemoveRange(notifications);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = $"Đã xóa {notifications.Count} thông báo đã đọc" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}
