using Microsoft.AspNetCore.SignalR;
using Bloomie.Data;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Hubs
{
    public class NotificationHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public NotificationHub(ApplicationDbContext context)
        {
            _context = context;
        }
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "AdminGroup");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, "AdminGroup");
            }
            await base.OnDisconnectedAsync(exception);
        }

        // Gửi thông báo cập nhật shipper cho đơn hàng cụ thể
        public async Task SendShipperUpdate(int orderId, object shipperInfo)
        {
            await Clients.All.SendAsync("ReceiveShipperUpdate", orderId, shipperInfo);
        }

        // Gửi thông báo cập nhật trạng thái đơn hàng và thanh toán
        public async Task SendOrderStatusUpdate(int orderId, object statusInfo)
        {
            await Clients.All.SendAsync("ReceiveOrderStatusUpdate", orderId, statusInfo);
        }

        // Gửi thông báo đơn hàng mới cho Admin
        public async Task SendNewOrder(object orderInfo)
        {
            await Clients.Group("AdminGroup").SendAsync("ReceiveNewOrder", orderInfo);
        }

        // Gửi vị trí GPS của shipper (realtime tracking)
        public async Task SendShipperLocation(int orderId, double latitude, double longitude)
        {
            // Lưu GPS vào database
            var order = await _context.Orders.FindAsync(orderId);
            if (order != null)
            {
                order.LastKnownLatitude = latitude;
                order.LastKnownLongitude = longitude;
                order.LastGPSUpdate = DateTime.Now;
                await _context.SaveChangesAsync();
            }
            
            // Broadcast tới tất cả clients
            await Clients.All.SendAsync("ReceiveShipperLocation", orderId, new
            {
                latitude = latitude,
                longitude = longitude,
                timestamp = DateTime.Now
            });
        }
    }
}
