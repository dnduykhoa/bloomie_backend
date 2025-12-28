using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class TwoFactorViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập")]
        public string UserName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã xác thực")]
        public string TwoFactorCode { get; set; }
    }
}