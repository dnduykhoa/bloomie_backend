using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class Rating
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public string UserId { get; set; } = null!;

    public int Star { get; set; }

    public string Comment { get; set; } = null!;

    public DateTime ReviewDate { get; set; }

    public string ImageUrl { get; set; } = null!;

    public int LikesCount { get; set; }

    public bool IsVisible { get; set; }

    public string LastModifiedBy { get; set; } = null!;

    public DateTime? LastModifiedDate { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<RatingImage> RatingImages { get; set; } = new List<RatingImage>();

    public virtual ICollection<Reply> Replies { get; set; } = new List<Reply>();

    public virtual ICollection<Report> ReportRatingId1Navigations { get; set; } = new List<Report>();

    public virtual ICollection<Report> ReportRatings { get; set; } = new List<Report>();

    public virtual AspNetUser User { get; set; } = null!;

    public virtual ICollection<UserLike> UserLikes { get; set; } = new List<UserLike>();
}
