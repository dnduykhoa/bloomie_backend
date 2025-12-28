using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class DeleteAccountViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập lý do xóa tài khoản.")]
        public string Reason { get; set; }
        
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu để xác nhận.")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}