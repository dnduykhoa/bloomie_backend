namespace Bloomie.Models.ViewModels
{
    public class ExportUsersRequest
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public bool IncludeDeleted { get; set; }
        public bool IncludeRoles { get; set; } = true;
        public string? RoleFilter { get; set; }
    }

    public class ExportLoginHistoryRequest
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string? UserId { get; set; }
    }

    public class ImportUsersRequest
    {
        public IFormFile? File { get; set; }
        public bool RequireEmailConfirmation { get; set; }
        public string DefaultRole { get; set; } = "User";
        public bool RequirePasswordChange { get; set; } = true;
    }

    public class ImportResult
    {
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> SuccessMessages { get; set; } = new();
    }

    public class UserImportModel
    {
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? PhoneNumber { get; set; }
        public string? Role { get; set; }
    }
}