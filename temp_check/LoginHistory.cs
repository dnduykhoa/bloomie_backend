using System;
using System.Collections.Generic;

namespace Bloomie.temp_check;

public partial class LoginHistory
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public DateTime LoginTime { get; set; }

    public string Ipaddress { get; set; } = null!;

    public string UserAgent { get; set; } = null!;

    public bool IsNewDevice { get; set; }

    public string SessionId { get; set; } = null!;

    public string Browser { get; set; } = null!;

    public string? Location { get; set; }
}
