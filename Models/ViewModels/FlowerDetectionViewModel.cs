namespace Bloomie.Models.ViewModels
{
    public class FlowerDetectionRequest
    {
        public IFormFile? ImageFile { get; set; }
    }

    public class FlowerDetectionResponse
    {
        public bool Success { get; set; }
        public int Total { get; set; }
        public Dictionary<string, int>? Counts { get; set; }
        public List<FlowerWithPriceViewModel>? FlowersWithPrices { get; set; }
        public decimal TotalValue { get; set; }
        public string? Message { get; set; }
    }

    public class FlowerWithPriceViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int? FlowerTypeId { get; set; }
        public decimal PricePerStem { get; set; }
        public decimal TotalPrice { get; set; }
        public FlowerPriceData? PriceData { get; set; }
    }

    public class FlowerPriceData
    {
        public string? FlowerTypeName { get; set; }
        public PriceRange? PricePerStem { get; set; }
    }

    public class PriceRange
    {
        public decimal Recommended { get; set; }
        public decimal Min { get; set; }
        public decimal Max { get; set; }
    }
}
