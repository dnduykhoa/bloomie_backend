using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class UserAccessLog
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public DateTime AccessTime { get; set; }

    public string Url { get; set; } = null!;

    public virtual AspNetUser User { get; set; } = null!;
}
