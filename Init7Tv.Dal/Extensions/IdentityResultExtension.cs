using Microsoft.AspNetCore.Identity;

namespace Init7Tv.Dal.Extensions;

public static class IdentityResultExtension
{
    public static string ToErrorString(this IdentityResult identityResult)
    {
        return string.Join(", ", identityResult.Errors.Select(e => e.Description));
    }
}