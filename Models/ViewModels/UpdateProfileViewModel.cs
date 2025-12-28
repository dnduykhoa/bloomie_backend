using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class UpdateProfileViewModel
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
}
