using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class ProductListItemViewModel
    {
        public Product Product { get; set; }
        public double AverageRating { get; set; }
        public int TotalReviews { get; set; }
        public int TotalSold { get; set; }
        public bool HasPromotion { get; set; }
    }
}
