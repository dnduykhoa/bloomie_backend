using Bloomie.Data;
using Bloomie.Services.Interfaces;
using Bloomie.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Hangfire;

namespace Bloomie.Services.Implementations
{
    public class OrderCancellationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;

        public OrderCancellationService(ApplicationDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        /// <summary>
        /// Kiểm tra và hủy đơn hàng nếu vẫn chưa thanh toán sau 30 phút
        /// </summary>
        public async Task CheckAndCancelPendingPaymentOrder(int orderId)
        {
            var order = await _context.Orders.FindAsync(orderId);
            
            if (order == null)
                return;

            // Chỉ hủy nếu trạng thái thanh toán vẫn là "Chờ thanh toán"
            if (order.PaymentStatus == "Chờ thanh toán")
            {
                order.Status = "Đã hủy";
                order.PaymentStatus = "Thanh toán thất bại";
                order.CancelReason = "Hệ thống tự động hủy: Quá thời gian thanh toán (30 phút)";
                order.CancelledAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // Gửi email thông báo cho khách hàng với template chuyên nghiệp
                var user = await _context.Users.FindAsync(order.UserId);
                var email = user?.Email;
                if (!string.IsNullOrEmpty(email))
                {
                    var subject = $"[Bloomie] Đơn hàng #{order.OrderId} đã bị hủy";
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
                                <h2>Đơn hàng của bạn đã bị hủy</h2>
                                <div class='order-info'>
                                    <strong>Mã đơn hàng:</strong> #{order.OrderId}<br/>
                                    <strong>Thời gian hủy:</strong> {DateTime.Now:HH:mm dd/MM/yyyy}<br/>
                                    <strong>Lý do:</strong> Quá hạn thanh toán (30 phút kể từ khi đặt hàng)<br/>
                                </div>
                                <p>Chúng tôi rất tiếc phải thông báo rằng đơn hàng của bạn đã bị hủy do không hoàn tất thanh toán trong thời gian quy định.</p>
                                <p>Nếu bạn vẫn muốn mua sản phẩm, vui lòng truy cập website và đặt lại đơn hàng mới.</p>
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

                // TODO: Hoàn trả số lượng sản phẩm vào kho nếu cần
            }
        }

        /// <summary>
        /// Đặt lịch hủy đơn hàng sau 30 phút
        /// </summary>
        public static string ScheduleCancellation(int orderId)
        {
            var jobId = BackgroundJob.Schedule<OrderCancellationService>(
                service => service.CheckAndCancelPendingPaymentOrder(orderId),
                TimeSpan.FromMinutes(30)
            );
            
            return jobId;
        }

        /// <summary>
        /// Hủy job đã đặt lịch khi thanh toán thành công
        /// </summary>
        public static void CancelScheduledJob(string jobId)
        {
            if (!string.IsNullOrEmpty(jobId))
            {
                BackgroundJob.Delete(jobId);
            }
        }
    }
}
