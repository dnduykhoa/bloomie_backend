using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bloomie.Models.Entities
{
    public class OrderAssignmentHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [ForeignKey("OrderId")]
        public Order? Order { get; set; }

        [Required]
        public string ShipperId { get; set; } = string.Empty;

        [ForeignKey("ShipperId")]
        public Data.ApplicationUser? Shipper { get; set; }

        [Required]
        public DateTime AssignedAt { get; set; }

        /// <summary>
        /// Response từ shipper: "Accepted", "Rejected", "Timeout"
        /// </summary>
        [MaxLength(20)]
        public string? Response { get; set; }

        public DateTime? RespondedAt { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
