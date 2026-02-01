namespace Init7Tv.Dto.Admin;

public sealed class GetUserDto : UserBaseDto
{
    public string Id { get; set; }
    public bool IsAdmin { get; set; }
}