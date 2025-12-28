namespace Bloomie.Services
{
    public class AutoReplyService
    {
        private readonly TimeSpan WorkingHoursStart = new TimeSpan(8, 0, 0); // 8:00 AM
        private readonly TimeSpan WorkingHoursEnd = new TimeSpan(21, 0, 0);  // 9:00 PM (21:00)

        /// <summary>
        /// Kiểm tra xem hiện tại có phải giờ làm việc không
        /// </summary>
        public bool IsWorkingHours()
        {
            var now = DateTime.Now.TimeOfDay;
            return now >= WorkingHoursStart && now < WorkingHoursEnd;
        }

        /// <summary>
        /// Lấy thông tin chi tiết về giờ làm việc
        /// </summary>
        public object GetWorkingHoursInfo()
        {
            var now = DateTime.Now;
            var isOpen = IsWorkingHours();
            var nextAvailable = GetNextAvailableTime();

            return new
            {
                isOpen = isOpen,
                status = isOpen ? "Đang hoạt động" : "Ngoài giờ làm việc",
                workingHours = "8:00 AM - 10:00 PM",
                workingDays = "Thứ 2 - Chủ nhật",
                todayStart = WorkingHoursStart.ToString(@"hh\:mm"),
                todayEnd = WorkingHoursEnd.ToString(@"hh\:mm"),
                nextAvailable = nextAvailable,
                currentTime = now.ToString("HH:mm"),
                timezone = "GMT+7"
            };
        }

        /// <summary>
        /// Lấy thời gian mở cửa tiếp theo
        /// </summary>
        public string GetNextAvailableTime()
        {
            var now = DateTime.Now;
            
            if (IsWorkingHours())
            {
                // Đang trong giờ làm việc
                var closeTime = now.Date.Add(WorkingHoursEnd);
                var timeUntilClose = closeTime - now;
                return $"Đóng cửa sau {timeUntilClose.Hours}h {timeUntilClose.Minutes}m";
            }
            else
            {
                // Ngoài giờ làm việc
                var nextOpen = now.TimeOfDay < WorkingHoursStart
                    ? now.Date.Add(WorkingHoursStart) // Mở cửa hôm nay
                    : now.Date.AddDays(1).Add(WorkingHoursStart); // Mở cửa ngày mai
                
                var timeUntilOpen = nextOpen - now;
                
                if (timeUntilOpen.TotalHours < 24)
                {
                    return $"Mở cửa sau {timeUntilOpen.Hours}h {timeUntilOpen.Minutes}m";
                }
                else
                {
                    return $"Mở cửa lúc {nextOpen:dd/MM HH:mm}";
                }
            }
        }

        /// <summary>
        /// Lấy tin nhắn auto reply khi ngoài giờ làm việc
        /// </summary>
        public string GetOutOfOfficeMessage()
        {
            return "Xin chào! 🌸 Cảm ơn bạn đã liên hệ với Bloomie.\n\n" +
                   "Hiện tại chúng mình đang ngoài giờ làm việc (8:00 - 22:00 hàng ngày).\n\n" +
                   "Tin nhắn của bạn đã được ghi nhận và chúng mình sẽ phản hồi bạn sớm nhất " +
                   "vào giờ làm việc tiếp theo. 💐\n\n" +
                   "Nếu cần hỗ trợ khẩn cấp, bạn có thể:\n" +
                   "📞 Gọi hotline: 1900-xxxx\n" +
                   "📧 Email: support@bloomie.vn\n\n" +
                   "Xin cảm ơn và chúc bạn một ngày tuyệt vời! ✨";
        }

        /// <summary>
        /// Lấy tin nhắn chào mừng khi khách hàng nhắn lần đầu
        /// </summary>
        public string GetWelcomeMessage()
        {
            return "Xin chào! 🌸 Cảm ơn bạn đã liên hệ với Bloomie.\n\n" +
                   "Chúng mình là đội ngũ tư vấn hoa tươi, rất vui được hỗ trợ bạn hôm nay!\n\n" +
                   "Bạn đang quan tâm đến sản phẩm hoặc dịch vụ nào của chúng mình ạ? 💐";
        }
    }
}
