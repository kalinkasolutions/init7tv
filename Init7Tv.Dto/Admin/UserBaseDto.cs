using System.ComponentModel.DataAnnotations;

namespace Init7Tv.Dto.Admin;

public class UserBaseDto
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "A user name is required")]
    public string UserName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "An email address is required")]
    [EmailAddress(ErrorMessage = "That is not a valid email address")]
    public string Email { get; set; } = string.Empty;

    public IList<string> Roles { get; set; } = [];
}