using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class ServiceReview
{
    public int ServiceReviewId { get; set; }

    public int OrderId { get; set; }

    public string UserId { get; set; } = null!;

    public int DeliveryRating { get; set; }

    public int ServiceRating { get; set; }

    public int OverallRating { get; set; }

    public string? Comment { get; set; }

    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Order Order { get; set; } = null!;

    public virtual AspNetUser User { get; set; } = null!;
}
