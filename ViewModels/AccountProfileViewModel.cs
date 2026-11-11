using System.ComponentModel.DataAnnotations;

namespace SmartDocumentRAG.API.ViewModels;

public class AccountProfileViewModel
{
    [Display(Name = "Họ và tên")]
    [MaxLength(150, ErrorMessage = "Họ và tên không được vượt quá 150 ký tự.")]
    public string? FullName { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [MaxLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự.")]
    [Display(Name = "Gmail / Email")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [MaxLength(30, ErrorMessage = "Số điện thoại không được vượt quá 30 ký tự.")]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }

    [MaxLength(100, ErrorMessage = "Tên trường không được vượt quá 100 ký tự.")]
    [Display(Name = "Trường đại học")]
    public string? University { get; set; }

    [MaxLength(300, ErrorMessage = "Địa chỉ không được vượt quá 300 ký tự.")]
    [Display(Name = "Địa chỉ")]
    public string? Address { get; set; }
}
