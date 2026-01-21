using Microsoft.AspNetCore.Identity;

namespace Init7Tv.Extensions;

public static class IdentityResultExtension
{
    public static string ToErrorText(this IdentityResult result)
    {
        return string.Join(Environment.NewLine, result.Errors.Select(x => x.Description));
    }
}