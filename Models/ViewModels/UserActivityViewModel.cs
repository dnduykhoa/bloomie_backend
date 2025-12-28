namespace Bloomie.Models.ViewModels
{
    public class UserActivityViewModel
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public string IpAddress { get; set; }
        public string DeviceInfo { get; set; }
        public string Status { get; set; }
        public string UserId { get; set; }
    }
}