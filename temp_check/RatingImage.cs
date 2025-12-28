using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class RatingImage
{
    public int Id { get; set; }

    public int RatingId { get; set; }

    public string ImageUrl { get; set; } = null!;

    public DateTime UploadedAt { get; set; }

    public virtual Rating Rating { get; set; } = null!;
}
