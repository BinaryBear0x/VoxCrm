using Hangfire.Annotations;
using Hangfire.Dashboard;

namespace VoxCrm.Web.Services;

public class HangfireAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize([NotNull] DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (!(httpContext.User?.Identity?.IsAuthenticated ?? false))
            return false;

        // Hangfire jobs span tenants and require platform-level authorization.
        return httpContext.User.IsInRole("SystemAdmin");
    }
}
