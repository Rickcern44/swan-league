using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swan.Core.Identity;
using Swan.Core.League;
using Swan.Data;

namespace Swan.Api.Authentication;

// Who may use the Setup routes: a global administrator, or the active commissioner of the season that is in Setup right now. The commissioner half is read from the database on every
// request (never from a claim), so losing the membership ends the access at once, and a commissioner of any other season gets nothing.
public sealed class SetupManagerRequirement : IAuthorizationRequirement;

// The database is looked up only when an administrator's role does not already settle the question.
public sealed class SetupManagerHandler(IServiceProvider services) : AuthorizationHandler<SetupManagerRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SetupManagerRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (context.User.IsInRole(nameof(UserRole.Admin))) { context.Succeed(requirement); return; }
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user)) return;
        var database = services.GetRequiredService<SwanDbContext>();
        var commissionsTheSetupSeason = await database.Seasons.AsNoTracking().AnyAsync(season =>
            season.State == SeasonState.Setup &&
            database.Memberships.Any(membership => membership.SeasonId == season.Id && membership.UserId == new Swan.Core.Common.UserId(user) && membership.Role == MembershipRole.Commissioner && membership.IsActive));
        if (commissionsTheSetupSeason) context.Succeed(requirement);
    }
}
