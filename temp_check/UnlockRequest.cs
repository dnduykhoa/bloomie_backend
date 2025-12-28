using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class UnlockRequest
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public string Token { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public string Status { get; set; } = null!;

    public string? AdminId { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? Note { get; set; }
}
