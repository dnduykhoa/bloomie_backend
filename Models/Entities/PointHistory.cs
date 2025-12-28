using System;
using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class PointHistory
    {
        public int Id { get; set; }
        public required string UserId { get; set; }
        public ApplicationUser? User { get; set; }
        public int Points { get; set; } // Số điểm (dương = cộng, âm = trừ)
        public required string Reason { get; set; } // Lý do: Check-in, Đổi quà, Sử dụng cho đơn hàng, v.v.
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public int? OrderId { get; set; } // Link tới Order nếu liên quan đến đơn hàng
        public Order? Order { get; set; }
    }
}
