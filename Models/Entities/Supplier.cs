using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public class Supplier
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public ICollection<PurchaseOrder>? PurchaseOrders { get; set; }
    }
}
