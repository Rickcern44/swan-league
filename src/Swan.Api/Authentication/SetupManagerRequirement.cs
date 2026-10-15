using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Services.Identity;

namespace Swan.Api.Authentication;

// Who may use the Setup routes: a global administrator, or the active commissioner of the season that is in Setup right now. The commissioner half is read from the database on every
// request (never from a claim), so losing the membership ends the access at once, and a commissioner of any other season gets nothing.
public sealed class SetupManagerRequirement : IAuthorizationRequirement;

// The lookup is made only when an administrator's role does not already settle the question, and it lives in the Services layer (the API does not touch the database).
public sealed class SetupManagerHandler(IServiceProvider services) : AuthorizationHandler<SetupManagerRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SetupManagerRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (context.User.IsInRole(nameof(UserRole.Admin))) { context.Succeed(requirement); return; }
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user)) return;
        var access = (ISetupAccessService)services.GetService(typeof(ISetupAccessService))!;
        if (await access.CommissionsTheSetupSeasonAsync(new UserId(user))) context.Succeed(requirement);
    }
}
