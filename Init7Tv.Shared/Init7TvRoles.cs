namespace Init7Tv.Shared;

public static class Init7TvRoles
{
    public const string Admin = "Admin";
    public const string User = "User";

    /// <summary>May plan recordings. Admins can do so without holding it.</summary>
    public const string Recording = "Recording";

    public static readonly string[] Roles = [Admin, User, Recording];
}