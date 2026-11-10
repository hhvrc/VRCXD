using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using VRCX.Common.Authentication;

namespace VRCX.API.Utils;

/// <summary>The Hangfire dashboard is reachable with the same API token as everything else.</summary>
public sealed class DashboardApiTokenAuth : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var result = await httpContext.AuthenticateAsync(VrcxAuthSchemes.ApiToken);
        return result.Succeeded;
    }
}
