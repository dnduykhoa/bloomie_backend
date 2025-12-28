using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class Reply
{
    public int Id { get; set; }

    public int RatingId { get; set; }

    public string UserId { get; set; } = null!;

    public string Comment { get; set; } = null!;

    public DateTime ReplyDate { get; set; }

    public int LikesCount { get; set; }

    public bool IsVisible { get; set; }

    public string LastModifiedBy { get; set; } = null!;

    public DateTime? LastModifiedDate { get; set; }

    public virtual Rating Rating { get; set; } = null!;

    public virtual ICollection<ReplyImage> ReplyImages { get; set; } = new List<ReplyImage>();

    public virtual AspNetUser User { get; set; } = null!;

    public virtual ICollection<UserLike> UserLikes { get; set; } = new List<UserLike>();
}
