using System.Net;
using System.Net.Mail;
using Bloomie.Services.Interfaces;

namespace Bloomie.Services.Implementations
{
    public class EmailService : IEmailService
    {
        public Task SendEmailAsync(string email, string subject, string message)
        {
            // Cấu hình SMTP client cho gmail
            var client = new SmtpClient("smtp.gmail.com", 587)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential("bloomieshop25@gmail.com", "nsma zysv cvdn lyvh")
            };

            // Tạo emaill message
            var mailMessage = new MailMessage
            {
                From = new MailAddress("bloomieshop25@gmail.com", "BLOOMIE SHOP"),
                Subject = subject,
                Body = message,
                IsBodyHtml = true
            };
            mailMessage.To.Add(email);

            return client.SendMailAsync(mailMessage);
        }
    }
}