using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class UserAccessLog
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public DateTime AccessTime { get; set; } // Thời gian truy cập
        public ApplicationUser User { get; set; }
        public string Url { get; set; } // Đường dẫn URL truy cập
    }
}