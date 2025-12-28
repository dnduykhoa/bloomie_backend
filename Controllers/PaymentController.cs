using Microsoft.AspNetCore.Mvc;
using Bloomie.Services.Interfaces;
using Bloomie.Models.Entities;
using Bloomie.Models.Momo;
using Bloomie.Models.Vnpay;
using Bloomie.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Bloomie.Hubs;

namespace Bloomie.Controllers
{
    public class PaymentController : Controller
    {
        private readonly IMomoService _momoService;
        private readonly IVNPAYService _vnpayService;
        private readonly IEmailService _emailService;
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public PaymentController(
            IMomoService momoService, 
            IVNPAYService vnpayService, 
            IEmailService emailService, 
            ApplicationDbContext context,
            IHubContext<NotificationHub> hubContext)
        {
            _momoService = momoService;
            _vnpayService = vnpayService;
            _emailService = emailService;
            _context = context;
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> CreateMomoPayment(OrderInfoModel model)
        {
            var result = await _momoService.CreatePaymentMomo(model);
            // Xử lý chuyển hướng hoặc trả về kết quả cho client
            return Json(result);
        }

        // [HttpPost]
        // public async Task<IActionResult> CreateVnpayPayment(OrderInfoModel model)
        // {
        //     var result = await _vnpayService.CreatePaymentVnpay(model);
        //     // Xử lý chuyển hướng hoặc trả về kết quả cho client
        //     return Json(result);
        // }

        [HttpGet]
        public async Task<IActionResult> MomoCallback()
        {
            // Lấy dữ liệu từ Momo gửi về qua query string (GET)
            var resultCode = Request.Query["resultCode"].ToString();
            var momoOrderId = Request.Query["orderId"].ToString();
            var message = Request.Query["message"].ToString();

            // Momo quy ước: resultCode == "0" là thành công
            if (resultCode == "0")
            {
                // Tách orderId gốc (trường hợp thanh toán lại có format: OrderId_timestamp)
                var originalOrderId = momoOrderId.Contains("_") 
                    ? momoOrderId.Split('_')[0] 
                    : momoOrderId;

                // Tìm đơn hàng theo OrderId gốc và cập nhật PaymentStatus
                var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == originalOrderId);
                if (order != null)
                {
                    order.PaymentStatus = "Đã thanh toán";
                    
                    // Hủy job tự động hủy đơn hàng nếu có
                    if (!string.IsNullOrEmpty(order.CancellationJobId))
                    {
                        Bloomie.Services.Implementations.OrderCancellationService.CancelScheduledJob(order.CancellationJobId);
                        order.CancellationJobId = null;
                    }
                    
                    _context.Orders.Update(order);
                    
                    // Trừ điểm nếu có sử dụng (cho thanh toán online)
                    if (order.PointsUsed > 0)
                    {
                        var userPoints = await _context.UserPoints.FirstOrDefaultAsync(up => up.UserId == order.UserId);
                        if (userPoints != null)
                        {
                            userPoints.TotalPoints -= order.PointsUsed;
                            userPoints.LastUpdated = DateTime.Now;
                            _context.UserPoints.Update(userPoints);
                            
                            // Ghi lại lịch sử sử dụng điểm
                            var pointHistory = new PointHistory
                            {
                                UserId = order.UserId!,
                                Points = -order.PointsUsed,
                                Reason = $"Sử dụng điểm cho đơn hàng {order.OrderId}",
                                CreatedDate = DateTime.Now,
                                OrderId = order.Id
                            };
                            _context.PointHistories.Add(pointHistory);
                        }
                    }
                    
                    await _context.SaveChangesAsync();
                    
                    // 🔔 Gửi SignalR notification cập nhật trạng thái thanh toán realtime
                    await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
                    {
                        orderStatus = order.Status,
                        paymentStatus = order.PaymentStatus
                    });
                    
                    TempData["success"] = "Thanh toán Momo thành công!";
                    return RedirectToAction("OrderSuccess", "Order", new { orderId = order.Id });
                }
                else
                {
                    TempData["error"] = "Không tìm thấy đơn hàng.";
                    return RedirectToAction("Index", "Order");
                }
            }
            else
            {
                // Thanh toán thất bại hoặc bị hủy
                var originalOrderId = momoOrderId.Contains("_") 
                    ? momoOrderId.Split('_')[0] 
                    : momoOrderId;
                var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == originalOrderId);
                if (order != null)
                {
                    order.PaymentStatus = "Chờ thanh toán";
                    _context.Orders.Update(order);
                    await _context.SaveChangesAsync();
                    
                    // 🔔 Gửi SignalR notification cập nhật trạng thái thanh toán realtime
                    await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
                    {
                        orderStatus = order.Status,
                        paymentStatus = order.PaymentStatus
                    });
                    
                    await SendPaymentFailedEmailAsync(order);
                }
                TempData["error"] = $"Thanh toán Momo thất bại. {message}";
                return RedirectToAction("Index", "Order");
            }
        }

        [HttpGet]
        public async Task<IActionResult> VnpayCallback()
        {
            // Lấy toàn bộ dữ liệu từ VNPAY gửi về qua query string
            var response = _vnpayService.PaymentExecute(Request.Query);

            if (response == null || response.VnPayResponseCode != "00")
            {
                // Thanh toán thất bại
                var failedOrderDescription = response?.OrderDescription ?? "";
                var failedOrderId = failedOrderDescription.Replace("Thanh toán đơn hàng ", "").Replace("Thanh toán lại đơn hàng ", "").Trim();
                var failedOrder = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == failedOrderId);
                if (failedOrder != null)
                {
                    failedOrder.PaymentStatus = "Chờ thanh toán";
                    _context.Orders.Update(failedOrder);
                    await _context.SaveChangesAsync();
                    
                    // 🔔 Gửi SignalR notification cập nhật trạng thái thanh toán realtime
                    await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", failedOrder.Id, new
                    {
                        orderStatus = failedOrder.Status,
                        paymentStatus = failedOrder.PaymentStatus
                    });
                    
                    await SendPaymentFailedEmailAsync(failedOrder);
                }
                TempData["error"] = $"Thanh toán VNPAY thất bại. Mã lỗi: {response?.VnPayResponseCode}";
                return RedirectToAction("Index", "Order");
            }

            // Thanh toán thành công - Parse OrderId từ OrderDescription
            // Format: "Thanh toán đơn hàng {OrderId}"
            var orderDescription = response.OrderDescription ?? "";
            var orderId = orderDescription.Replace("Thanh toán đơn hàng ", "").Replace("Thanh toán lại đơn hàng ", "").Trim();

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order != null)
            {
                order.PaymentStatus = "Đã thanh toán";

                // Hủy job tự động hủy đơn hàng nếu có
                if (!string.IsNullOrEmpty(order.CancellationJobId))
                {
                    Bloomie.Services.Implementations.OrderCancellationService.CancelScheduledJob(order.CancellationJobId);
                    order.CancellationJobId = null;
                }

                _context.Orders.Update(order);
                
                // Trừ điểm nếu có sử dụng (cho thanh toán online)
                if (order.PointsUsed > 0)
                {
                    var userPoints = await _context.UserPoints.FirstOrDefaultAsync(up => up.UserId == order.UserId);
                    if (userPoints != null)
                    {
                        userPoints.TotalPoints -= order.PointsUsed;
                        userPoints.LastUpdated = DateTime.Now;
                        _context.UserPoints.Update(userPoints);
                        
                        // Ghi lại lịch sử sử dụng điểm
                        var pointHistory = new PointHistory
                        {
                            UserId = order.UserId!,
                            Points = -order.PointsUsed,
                            Reason = $"Sử dụng điểm cho đơn hàng {order.OrderId}",
                            CreatedDate = DateTime.Now,
                            OrderId = order.Id
                        };
                        _context.PointHistories.Add(pointHistory);
                    }
                }
                
                await _context.SaveChangesAsync();

                // 🔔 Gửi SignalR notification cập nhật trạng thái thanh toán realtime
                await _hubContext.Clients.All.SendAsync("ReceiveOrderStatusUpdate", order.Id, new
                {
                    orderStatus = order.Status,
                    paymentStatus = order.PaymentStatus
                });

                TempData["success"] = "Thanh toán VNPAY thành công!";
                return RedirectToAction("OrderSuccess", "Order", new { orderId = order.Id });
            }
            else
            {
                TempData["error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index", "Order");
            }
        }

        private async Task SendPaymentFailedEmailAsync(Order order)
        {
            var user = await _context.Users.FindAsync(order.UserId);
            var email = user?.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var subject = $"[Bloomie] Thanh toán đơn hàng #{order.OrderId} thất bại";
                var body = $@"
                <!DOCTYPE html>
                <html lang='vi'>
                <head>
                    <meta charset='UTF-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>
                        body {{ font-family: Arial, sans-serif; background-color: #f4f4f4; margin: 0; padding: 0; }}
                        .container {{ max-width: 600px; margin: 30px auto; background-color: #fff; border-radius: 10px; box-shadow: 0 4px 12px rgba(0,0,0,0.08); overflow: hidden; }}
                        .header {{ background-color: #FF7043; padding: 24px; text-align: center; }}
                        .header h1 {{ color: #fff; margin: 0; font-size: 28px; }}
                        .content {{ padding: 32px; color: #333; }}
                        .order-info {{ background-color: #f8f9fa; padding: 18px; border-radius: 6px; margin: 18px 0; }}
                        .footer {{ background-color: #f8f8f8; padding: 18px; text-align: center; font-size: 15px; color: #777; }}
                        .btn {{ display: inline-block; padding: 12px 24px; background-color: #FF7043; color: #fff !important; text-decoration: none; font-size: 16px; font-weight: bold; border-radius: 5px; margin: 10px 5px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h1>Bloomie Flower Shop</h1>
                        </div>
                        <div class='content'>
                            <h2>Thanh toán đơn hàng thất bại</h2>
                            <div class='order-info'>
                                <strong>Mã đơn hàng:</strong> #{order.OrderId}<br/>
                                <strong>Thời gian:</strong> {DateTime.Now:HH:mm dd/MM/yyyy}<br/>
                                <strong>Phương thức thanh toán:</strong> {order.PaymentMethod}<br/>
                            </div>
                            <p>Chúng tôi rất tiếc phải thông báo rằng thanh toán cho đơn hàng của bạn đã thất bại.</p>
                            <p>Vui lòng thử lại hoặc chọn phương thức thanh toán khác để hoàn tất đơn hàng.</p>
                            <p>Nếu có thắc mắc hoặc cần hỗ trợ, hãy liên hệ với chúng tôi qua:</p>
                            <ul>
                                <li>📞 Hotline: <strong>0987 654 321</strong></li>
                                <li>📧 Email: <strong>bloomieshop25@gmail.com</strong></li>
                            </ul>
                            <div style='text-align:center; margin: 30px 0;'>
                                <a href='https://bloomie.vn' class='btn'>Quay lại Bloomie Shop</a>
                            </div>
                        </div>
                        <div class='footer'>
                            <p>© 2025 Bloomie Flower Shop. Email này được gửi tự động, vui lòng không trả lời.</p>
                        </div>
                    </div>
                </body>
                </html>
                ";
                await _emailService.SendEmailAsync(email, subject, body);
            }
        }
    }
}
