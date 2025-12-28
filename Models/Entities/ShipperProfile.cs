using System;

namespace Bloomie.Models.Entities
{
    public class ShipperProfile
    {
        public int Id { get; set; }
        public string UserId { get; set; } = null!; // ID của user có role Shipper
        public bool IsWorking { get; set; } = true; // Đang làm việc hay không
        public int MaxActiveOrders { get; set; } = 2; // Số đơn tối đa có thể giao cùng lúc
        public int CurrentActiveOrders { get; set; } = 0; // Số đơn đang giao hiện tại (cache)
        public DateTime? LastAssignedAt { get; set; } // Lần cuối được assign đơn (dùng cho round robin)
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
    }
}
