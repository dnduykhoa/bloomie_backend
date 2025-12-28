using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ApiRequests
{
    public class LocationUpdateRequest
    {
        [Required(ErrorMessage = "Vui lòng cung cấp Order ID.")]
        public int OrderId { get; set; }
        
        [Required(ErrorMessage = "Vui lòng cung cấp vĩ độ.")]
        [Range(-90, 90, ErrorMessage = "Vĩ độ phải nằm trong khoảng -90 đến 90.")]
        public double Latitude { get; set; }
        
        [Required(ErrorMessage = "Vui lòng cung cấp kinh độ.")]
        [Range(-180, 180, ErrorMessage = "Kinh độ phải nằm trong khoảng -180 đến 180.")]
        public double Longitude { get; set; }
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
    
    public class ShipperLocationResponse
    {
        public int OrderId { get; set; }
        public string? OrderNumber { get; set; }
        public string? CustomerName { get; set; }
        public string? DeliveryAddress { get; set; }
        public double? CurrentLatitude { get; set; }
        public double? CurrentLongitude { get; set; }
        public DateTime? LastLocationUpdate { get; set; }
        public string? Status { get; set; }
        public string? ShipperStatus { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? DeliveryDate { get; set; }  // ✅ Ngày giao hàng hoàn thành
    }
}