namespace Bloomie.Models.ViewModels
{
    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string? UserId { get; set; }
    }

    public class ChatResponse
    {
        public string Message { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string? Intent { get; set; }
        public List<QuickReply>? QuickReplies { get; set; }
        public List<ProductSuggestion>? Products { get; set; }
        public int? CartCount { get; set; }
        public object? Vouchers { get; set; }
    }

    public class QuickReply
    {
        public string Text { get; set; } = string.Empty;
        public string? Icon { get; set; }
    }

    public class ProductSuggestion
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string? ImageUrl { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}
