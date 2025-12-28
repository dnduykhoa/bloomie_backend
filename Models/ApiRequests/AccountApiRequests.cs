using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ApiRequests
{
    public class EnableTwoFactorRequest
    {
        public string UserName { get; set; } = string.Empty;
        public bool Enable { get; set; }
    }
        
    public class ResendTwoFactorRequest
    {
        public string UserName { get; set; } = string.Empty;
    }
        
    public class ChangePasswordRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string OldPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
        
    public class ForgotPasswordRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
        
    public class RestoreRequest
    {
        public string Email { get; set; } = string.Empty;
    }
        
    public class LogoutSessionRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
    }
        
    public class LogoutAllSessionsRequest
    {
        public string UserName { get; set; } = string.Empty;
    }
        
    public class UpdateProfileImageRequest
    {
        public string UserName { get; set; } = string.Empty;
        public IFormFile ProfileImage { get; set; } = null!;
    }
        
    public class DeleteProfileImageRequest
    {
        public string UserName { get; set; } = string.Empty;
    }

    public class DeleteAccountViewModel
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class NewPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
    
    public class UpdateProfileRequest
    {
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
        public required string UserName { get; set; }

        // Tên đăng nhập mới (nếu muốn đổi)
        public string? NewUserName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
        public required string FullName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Vui lòng nhập địa chỉ email hợp lệ.")]
        public required string Email { get; set; }

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        [RegularExpression(@"^(\+84|84|0)(3|5|7|8|9)([0-9]{8})$", ErrorMessage = "Số điện thoại phải là số Việt Nam hợp lệ (10-11 số)")]
        public string? PhoneNumber { get; set; }
    }

    public class VerifyOtpRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
    }

    public class GoogleLoginRequest
    {
        [Required(ErrorMessage = "Vui lòng cung cấp Google ID Token.")]
        public required string IdToken { get; set; }
        
        [Required(ErrorMessage = "Vui lòng cung cấp email.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        public required string Email { get; set; }
        
        public string? DisplayName { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("deviceName")]
        public string? DeviceName { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("clientIPAddress")]
        public string? ClientIPAddress { get; set; }
    }
}
