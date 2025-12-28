using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class ReplyImage
{
    public int Id { get; set; }

    public int ReplyId { get; set; }

    public string ImageUrl { get; set; } = null!;

    public DateTime UploadedAt { get; set; }

    public virtual Reply Reply { get; set; } = null!;
}
