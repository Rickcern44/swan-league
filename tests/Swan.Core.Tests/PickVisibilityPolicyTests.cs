using Swan.Core.Common;
using Swan.Core.Picks;
using Xunit;

namespace Swan.Core.Tests;

public sealed class PickVisibilityPolicyTests
{
    private static readonly DateTimeOffset Now = new(2025, 10, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CoachCannotSeeLockedTeamPickBeforeTheirOwnPicksAreAllLocked()
    {
        var viewer = new[] { TeamPickAt(-1), TeamPickAt(1), TeamPickAt(2) };

        Assert.False(PickVisibilityPolicy.CanSeeTeamPick(viewer, TeamPickAt(-1), Now, false, false));
    }

    [Fact]
    public void CoachSeesLockedTeamPickAfterAllOwnTeamPicksLock()
    {
        var viewer = new[] { TeamPickAt(-3), TeamPickAt(-2), TeamPickAt(-1) };

        Assert.True(PickVisibilityPolicy.CanSeeTeamPick(viewer, TeamPickAt(-1), Now, false, false));
    }

    [Fact]
    public void PartialEarlyGameLockDoesNotRevealOtherTeamPicks()
    {
        var viewer = new[] { TeamPickAt(-1), TeamPickAt(2), TeamPickAt(3) };

        Assert.False(PickVisibilityPolicy.CanSeeTeamPick(viewer, TeamPickAt(-1), Now, false, false));
    }

    [Fact]
    public void DogsAreIndependentFromTeamPicks()
    {
        var lockedTeams = new[] { TeamPickAt(-3), TeamPickAt(-2), TeamPickAt(-1) };
        var viewerDogs = new[] { DogPickAt(1) };

        Assert.True(PickVisibilityPolicy.CanSeeTeamPick(lockedTeams, TeamPickAt(-1), Now, false, false));
        Assert.False(PickVisibilityPolicy.CanSeeDogPick(viewerDogs, DogPickAt(-1), Now, false, false));
    }

    [Fact]
    public void AdminAndWeekOverrideSeeLockedPicks()
    {
        var viewer = new[] { TeamPickAt(1) };

        Assert.True(PickVisibilityPolicy.CanSeeTeamPick(viewer, TeamPickAt(1), Now, true, false));
        Assert.True(PickVisibilityPolicy.CanSeeTeamPick(viewer, TeamPickAt(-1), Now, false, true));
    }

    [Fact]
    public void CoachNeverSeesAnUnlockedTargetPick()
    {
        var viewer = new[] { TeamPickAt(-3), TeamPickAt(-2), TeamPickAt(-1) };

        Assert.False(PickVisibilityPolicy.CanSeeTeamPick(viewer, TeamPickAt(1), Now, false, false));
    }

    private static TeamPick TeamPickAt(int hours) => new(new TeamId(Guid.NewGuid()), new GameId(Guid.NewGuid()), Now.AddHours(hours));
    private static DogPick DogPickAt(int hours) => new(new TeamId(Guid.NewGuid()), new GameId(Guid.NewGuid()), Now.AddHours(hours));
}
