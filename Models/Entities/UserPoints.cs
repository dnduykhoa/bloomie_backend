using System;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class UserPoints
    {
        public int Id { get; set; }
        public required string UserId { get; set; }
        public required ApplicationUser User { get; set; }
        public int TotalPoints { get; set; } = 0;
        public int LifetimePoints { get; set; } = 0;
        public DateTime? LastCheckInDate { get; set; }
        public int ConsecutiveCheckIns { get; set; } = 0;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
