namespace Bloomie.Models.Entities
{
    public class UnlockRequest
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string Token { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime ExpiresAt { get; set; } 
        public string Status { get; set; } = "Pending"; // Pending, Completed, Rejected
        public string? AdminId { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? Note { get; set; }
    }
}