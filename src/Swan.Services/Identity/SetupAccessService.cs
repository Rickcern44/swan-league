using Microsoft.EntityFrameworkCore;
using Swan.Core.Common;
using Swan.Core.League;
using Swan.Data;

namespace Swan.Services.Identity;

public interface ISetupAccessService
{
    // True when this user is an active commissioner of the season that is in Setup right now. Read from the database on every call, never from a claim.
    Task<bool> CommissionsTheSetupSeasonAsync(UserId user, CancellationToken cancellationToken = default);
}

public sealed class SetupAccessService(SwanDbContext database) : ISetupAccessService
{
    public Task<bool> CommissionsTheSetupSeasonAsync(UserId user, CancellationToken cancellationToken = default) =>
        database.Seasons.AsNoTracking().AnyAsync(season =>
            season.State == SeasonState.Setup &&
            database.Memberships.Any(membership => membership.SeasonId == season.Id && membership.UserId == user && membership.Role == MembershipRole.Commissioner && membership.IsActive), cancellationToken);
}
