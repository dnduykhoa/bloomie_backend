using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class Order
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    public DateTime OrderDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Status { get; set; }

    public string? PaymentMethod { get; set; }

    public string? ShippingAddress { get; set; }

    public string? Phone { get; set; }

    public string? Note { get; set; }

    public string? OrderId { get; set; }

    public string? PaymentStatus { get; set; }

    public string? CancellationJobId { get; set; }

    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();

    public virtual ICollection<ServiceReview> ServiceReviews { get; set; } = new List<ServiceReview>();
}
