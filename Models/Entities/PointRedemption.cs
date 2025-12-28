using System;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class PointRedemption
    {
        public int Id { get; set; }
        public required string UserId { get; set; }
        public required ApplicationUser User { get; set; }
        public int PointRewardId { get; set; }
        public required PointReward PointReward { get; set; }
        public int PointsSpent { get; set; }
        public DateTime RedeemedDate { get; set; } = DateTime.Now;
        public int? UserVoucherId { get; set; }
        public UserVoucher? UserVoucher { get; set; }
    }
}
