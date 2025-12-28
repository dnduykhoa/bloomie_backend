using Microsoft.AspNetCore.Identity;

namespace Bloomie.Data
{
    public class ApplicationUser : IdentityUser
    {
        public required string FullName { get; set; }
        public required string RoleId { get; set; }
        public required string Token { get; set; }
        public DateTime? TokenCreatedAt { get; set; }
        public string? ProfileImageUrl { get; set; }
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public string? DeleteReason { get; set; }
        public bool RequirePasswordChange { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Super Admin (Admin gốc, không thể bị xóa hoặc thay đổi quyền)
        public bool IsSuperAdmin { get; set; } = false;
        
        // Audit fields - Theo dõi ai tạo/sửa user này
        public string? CreatedByUserId { get; set; }
        public DateTime? LastModifiedDate { get; set; }
        public string? LastModifiedByUserId { get; set; }
        
        // Online status tracking
        public DateTime? LastSeenAt { get; set; }
        
        // Chat spam control
        public bool IsBlockedFromChat { get; set; } = false;
        public DateTime? BlockedFromChatAt { get; set; }
        public string? BlockedFromChatReason { get; set; }
        public string? BlockedByUserId { get; set; }
    }
}