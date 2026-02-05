namespace Init7Tv.BusinessLogic;

public interface IUserIdentityProvider
{
    public string[] UserRoles { get; }
    public bool IsAdmin { get; }
    public string UserName { get; }
}