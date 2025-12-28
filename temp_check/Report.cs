using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class Report
{
    public int Id { get; set; }

    public int RatingId { get; set; }

    public string ReporterId { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public DateTime ReportDate { get; set; }

    public bool IsResolved { get; set; }

    public string ResolvedBy { get; set; } = null!;

    public DateTime? ResolvedDate { get; set; }

    public int? RatingId1 { get; set; }

    public virtual Rating Rating { get; set; } = null!;

    public virtual Rating? RatingId1Navigation { get; set; }

    public virtual AspNetUser Reporter { get; set; } = null!;
}
