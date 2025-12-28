using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Bloomie.Models.ViewModels
{
    public class LoginViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
        public string UserName { get; set; }

        [DataType(DataType.Password), Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        public string Password { get; set; }
        
        public bool RememberMe { get; set; }
        public string? ReturnUrl { get; set; }
        
        [JsonPropertyName("deviceName")]
        public string? DeviceName { get; set; }
        
        [JsonPropertyName("clientIPAddress")]
        public string? ClientIPAddress { get; set; }
    }
}