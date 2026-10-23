using Hangfire.Dashboard;

namespace PingWatch.Api.Filters;

public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // For development and demonstration purposes, allow all access.
        // In a production environment, plug in JWT / Cookie / Basic Authentication here.
        return true;
    }
}
