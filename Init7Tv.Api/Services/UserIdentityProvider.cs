using Init7Tv.BusinessLogic;

namespace Init7Tv.Services;

public sealed class UserIdentityProvider : IUserIdentityProvider
{
    private readonly IHttpContextAccessor m_httpContextAccessor;

    public UserIdentityProvider(IHttpContextAccessor httpContextAccessor)
    {
        m_httpContextAccessor = httpContextAccessor;
    }

    public string[] UserRoles
    {
        get
        {
            var user = m_httpContextAccessor.HttpContext?.User;

            if (user == null)
            {
                return [];
            }

            return user.Claims
                .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value)
                .ToArray();
        }
    }

    public bool IsAdmin  => m_httpContextAccessor.HttpContext?.User?.IsInRole("Admin") ?? false;
}