using System.ComponentModel.DataAnnotations.Schema;

namespace Bloomie.Models.Entities
{
    public class PromotionCategory
    {
        public int Id { get; set; }
        public int PromotionId { get; set; }
        public int CategoryId { get; set; }
        [ForeignKey("PromotionId")]
        public Promotion Promotion { get; set; }
        [ForeignKey("CategoryId")]
        public Category Category { get; set; }
    }
}
