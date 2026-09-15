namespace Init7Tv.Dto.Admin;

public sealed class UpdateUserDto : UserBaseDto
{
    /// <summary>Blank leaves the existing password in place.</summary>
    public string Password { get; set; } = string.Empty;
}