namespace Bloomie.Models.ViewModels
{
    public class RevenueViewModel
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal ActualRevenue { get; set; }
        public decimal TotalReturned { get; set; }
        public decimal TotalPromotionDiscount { get; set; }
        public decimal TotalPointsValue { get; set; }
        public decimal TotalCost { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal ProfitMargin { get; set; }
        public int TotalOrders { get; set; }
        public decimal AverageOrderValue { get; set; }
        public decimal PreviousRevenue { get; set; }
        public decimal RevenueGrowth { get; set; }
        public List<RevenueByDateViewModel> RevenueByDate { get; set; } = new List<RevenueByDateViewModel>();
        public List<RevenueByProductViewModel> RevenueByProduct { get; set; } = new List<RevenueByProductViewModel>();
        public List<RevenueByCategoryViewModel> RevenueByCategory { get; set; } = new List<RevenueByCategoryViewModel>();
        public List<RevenueByPaymentMethodViewModel> RevenueByPaymentMethod { get; set; } = new List<RevenueByPaymentMethodViewModel>();
        public List<OrderByStatusViewModel> OrdersByStatus { get; set; } = new List<OrderByStatusViewModel>();
        public List<TopCustomerViewModel> TopCustomers { get; set; } = new List<TopCustomerViewModel>();
    }

    public class RevenueByDateViewModel
    {
        public DateTime Date { get; set; }
        public decimal Revenue { get; set; }
        public int OrderCount { get; set; }
    }

    public class RevenueByProductViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Revenue { get; set; }
    }

    public class RevenueByCategoryViewModel
    {
        public string CategoryName { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
    }

    public class RevenueByPaymentMethodViewModel
    {
        public string PaymentMethod { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal Revenue { get; set; }
    }

    public class OrderByStatusViewModel
    {
        public string Status { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public class TopCustomerViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalSpent { get; set; }
    }
}
