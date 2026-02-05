using Init7Tv.Hubs;
using Init7Tv.Shared;
using Microsoft.AspNetCore.Authorization;

namespace Init7Tv.Endpoints;

public static class DashboardEndpoint
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/hub/admin/")
            .WithTags("Dashboard")
            .RequireAuthorization(new AuthorizeAttribute { Roles = Init7TvRoles.Admin });

        group.MapHub<DashboardHub>("/dashboard");
    }
}