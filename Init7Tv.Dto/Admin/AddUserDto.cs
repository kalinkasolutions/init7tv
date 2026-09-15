using System.ComponentModel.DataAnnotations;

namespace Init7Tv.Dto.Admin;

public sealed class AddUserDto : UserBaseDto
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "A password is required")]
    public string Password { get; set; } = string.Empty;
}