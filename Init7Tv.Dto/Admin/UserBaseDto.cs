namespace Init7Tv.Dto.Admin;

public class UserBaseDto
{
    public string UserName { get; set; }
    public string Email { get; set; }
    public IList<string> Roles { get; set; } = [];
}