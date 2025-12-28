using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class UserLike
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public int? RatingId { get; set; }

    public int? ReplyId { get; set; }

    public DateTime LikedAt { get; set; }

    public virtual Rating? Rating { get; set; }

    public virtual Reply? Reply { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
