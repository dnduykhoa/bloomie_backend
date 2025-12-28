using System;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class UserCheckIn
    {
        public int Id { get; set; }
        public required string UserId { get; set; }
        public required ApplicationUser User { get; set; }
        public DateTime CheckInDate { get; set; }
        public int PointsEarned { get; set; }
        public int ConsecutiveDays { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
