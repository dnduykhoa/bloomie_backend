namespace Bloomie.Models.Entities
{
    public class LoginHistory
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public DateTime LoginTime { get; set; }
        public string IPAddress { get; set; }
        public string UserAgent { get; set; }
        public bool IsNewDevice { get; set; }
        public string SessionId { get; set; }
        public string Browser { get; set; }
        public string? Location { get; set; }
    }
}