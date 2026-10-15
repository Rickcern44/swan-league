using Swan.Core.Common;
using Swan.Core.League;
using Swan.Core.Picks;
using Xunit;

namespace Swan.Core.Tests;

public sealed class RosterDraftTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = Now.AddDays(3);
    private readonly List<TeamId> catalog = Enumerable.Range(0, 32).Select(_ => new TeamId(Guid.NewGuid())).ToList();
    private IReadOnlySet<TeamId> SeasonTeams => catalog.ToHashSet();
    private Roster NewRoster() => new(new RosterId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), new MembershipId(Guid.NewGuid()));
    private TeamId[] Nine(int from = 0) => catalog.Skip(from).Take(9).ToArray();

    private static Season FinishedSetupSeason()
    {
        var season = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026 Swan League", false).Value!;
        Assert.True(season.SaveDoubleDogWeeks([2, 12, 17]).IsSuccess);
        Assert.True(season.CompleteSetup().IsSuccess);
        return season;
    }

    [Fact]
    public void ASeasonStartsItsRosterDraftWithAFutureDeadlineAndAnActiveCommissioner()
    {
        var season = FinishedSetupSeason();

        var started = season.StartRosterDraft(Deadline, Now, hasActiveCommissioner: true);

        Assert.True(started.IsSuccess);
        Assert.Equal((SeasonState.Live, Deadline), (season.State, season.RosterDraftDeadline));
        Assert.True(season.RosterDraftOpen(Now));
        Assert.False(season.RosterDraftOpen(Deadline));
        Assert.False(season.RosterDraftOpen(Deadline.AddMinutes(1)));
        Assert.Contains(season.DomainEvents, item => item is SeasonActivated);
    }

    [Theory]
    [InlineData(0, "season.draft-deadline")]
    [InlineData(-1, "season.draft-deadline")]
    [InlineData(-86400, "season.draft-deadline")]
    public void ADeadlineThatIsNotInTheFutureIsRefusedAndTheSeasonStaysInSetup(int secondsFromNow, string code)
    {
        var season = FinishedSetupSeason();

        var refused = season.StartRosterDraft(Now.AddSeconds(secondsFromNow), Now, true);

        Assert.Equal(code, refused.Error!.Code);
        Assert.Equal((SeasonState.Setup, null), (season.State, season.RosterDraftDeadline));
    }

    [Fact]
    public void ADeadlineMoreThanAYearAwayIsRefused()
    {
        var season = FinishedSetupSeason();

        Assert.Equal("season.draft-deadline", season.StartRosterDraft(Now.AddDays(367), Now, true).Error!.Code);
        Assert.Equal(SeasonState.Setup, season.State);
        Assert.True(season.StartRosterDraft(Now.AddDays(365), Now, true).IsSuccess);
    }

    [Fact]
    public void ASeasonWithoutAnActiveCommissionerCannotStart()
    {
        var season = FinishedSetupSeason();

        Assert.Equal("season.commissioner", season.StartRosterDraft(Deadline, Now, hasActiveCommissioner: false).Error!.Code);
        Assert.Equal(SeasonState.Setup, season.State);
    }

    [Fact]
    public void OnlyAFinishedSetupSeasonStartsAndOnlyOnce()
    {
        var unfinished = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026", false).Value!;
        unfinished.SaveDoubleDogWeeks([2, 12, 17]);
        var started = FinishedSetupSeason();
        started.StartRosterDraft(Deadline, Now, true);

        Assert.Equal("season.setup", unfinished.StartRosterDraft(Deadline, Now, true).Error!.Code);
        Assert.Equal("season.invalid", started.StartRosterDraft(Deadline.AddDays(1), Now, true).Error!.Code);
        Assert.Equal(Deadline, started.RosterDraftDeadline);
    }

    [Fact]
    public void ASeasonThatNeverHadADraftDeadlineHasNoOpenDraft()
    {
        var legacy = Season.CreateBlank(new SeasonId(Guid.NewGuid()), 2026, "2026", false).Value!;
        legacy.ActivateWithDogs();

        Assert.False(legacy.RosterDraftOpen(Now));
        Assert.Null(legacy.RosterDraftDeadline);
    }

    [Fact]
    public void ACoachSavesExactlyNineDistinctSeasonTeamsAndMayReviseWhileTheDraftIsOpen()
    {
        var roster = NewRoster();

        Assert.True(roster.SaveDraft(Nine(0), SeasonTeams, draftOpen: true).IsSuccess);
        Assert.True(roster.SaveDraft(Nine(5), SeasonTeams, draftOpen: true).IsSuccess);

        Assert.Equal(Nine(5).ToHashSet(), roster.Teams.ToHashSet());
        Assert.Equal(2, roster.Revision);
        Assert.False(roster.IsLocked);
    }

    [Theory]
    [InlineData(8, "roster.count")]
    [InlineData(10, "roster.count")]
    [InlineData(0, "roster.count")]
    public void AnythingButNineTeamsIsRefusedAndTheSavedRosterIsUntouched(int count, string code)
    {
        var roster = NewRoster();
        roster.SaveDraft(Nine(0), SeasonTeams, true);

        var refused = roster.SaveDraft(catalog.Skip(10).Take(count).ToArray(), SeasonTeams, true);

        Assert.Equal(code, refused.Error!.Code);
        Assert.Equal(Nine(0).ToHashSet(), roster.Teams.ToHashSet());
        Assert.Equal(1, roster.Revision);
    }

    [Fact]
    public void DuplicateAndUnknownTeamsAreRefused()
    {
        var roster = NewRoster();
        var duplicate = Nine(0).Take(8).Append(catalog[0]).ToArray();
        var unknown = Nine(0).Take(8).Append(new TeamId(Guid.NewGuid())).ToArray();

        Assert.Equal("roster.duplicate", roster.SaveDraft(duplicate, SeasonTeams, true).Error!.Code);
        Assert.Equal("roster.team", roster.SaveDraft(unknown, SeasonTeams, true).Error!.Code);
        Assert.Empty(roster.Teams);
    }

    [Fact]
    public void SavingTheSameNineAgainChangesNothing()
    {
        var roster = NewRoster();
        roster.SaveDraft(Nine(0), SeasonTeams, true);

        Assert.True(roster.SaveDraft(Nine(0).Reverse().ToArray(), SeasonTeams, true).IsSuccess);

        Assert.Equal(1, roster.Revision);
    }

    [Fact]
    public void ASaveAfterTheDeadlineIsRefused()
    {
        var roster = NewRoster();
        roster.SaveDraft(Nine(0), SeasonTeams, true);

        var late = roster.SaveDraft(Nine(5), SeasonTeams, draftOpen: false);

        Assert.Equal("roster.locked", late.Error!.Code);
        Assert.Equal(Nine(0).ToHashSet(), roster.Teams.ToHashSet());
    }

    [Fact]
    public void LockingNeedsExactlyNineTeamsAndTakesEffectAtOnce()
    {
        var roster = NewRoster();
        Assert.Equal("roster.count", roster.Lock(true, Now).Error!.Code);
        roster.SaveDraft(Nine(0), SeasonTeams, true);

        Assert.True(roster.Lock(true, Now).IsSuccess);

        Assert.True(roster.IsLocked);
        Assert.Equal(Now, roster.LockedAt);
        Assert.Equal("roster.locked", roster.SaveDraft(Nine(5), SeasonTeams, true).Error!.Code);
        Assert.Equal(Nine(0).ToHashSet(), roster.Teams.ToHashSet());
    }

    [Fact]
    public void ASecondLockOrALockAfterTheDeadlineIsRefused()
    {
        var roster = NewRoster();
        roster.SaveDraft(Nine(0), SeasonTeams, true);
        roster.Lock(true, Now);
        var late = NewRoster();
        late.SaveDraft(Nine(0), SeasonTeams, true);

        Assert.Equal("roster.locked", roster.Lock(true, Now.AddMinutes(1)).Error!.Code);
        Assert.Equal("roster.locked", late.Lock(draftOpen: false, Now).Error!.Code);
        Assert.False(late.IsLocked);
    }

    [Fact]
    public void ACommissionerReplacesOneTeamWithAnotherForAReasonEvenOnALockedRoster()
    {
        var roster = NewRoster();
        roster.SaveDraft(Nine(0), SeasonTeams, true);
        roster.Lock(true, Now);

        var corrected = roster.CorrectByCommissioner(catalog[0], catalog[20], SeasonTeams, "Coach picked the wrong team");

        Assert.True(corrected.IsSuccess);
        Assert.Contains(catalog[20], roster.Teams);
        Assert.DoesNotContain(catalog[0], roster.Teams);
        Assert.Equal(9, roster.Teams.Count);
        Assert.Equal(3, roster.Revision);
        Assert.True(roster.IsLocked);
    }

    [Fact]
    public void ACommissionerCanCompleteAnIncompleteRosterByAddingATeam()
    {
        var roster = NewRoster();

        Assert.True(roster.CorrectByCommissioner(null, catalog[3], SeasonTeams, "Never drafted").IsSuccess);
        Assert.Single(roster.Teams);
        for (var i = 4; i < 12; i++) Assert.True(roster.CorrectByCommissioner(null, catalog[i], SeasonTeams, "Never drafted").IsSuccess);
        Assert.Equal(9, roster.Teams.Count);
        Assert.Equal("roster.count", roster.CorrectByCommissioner(null, catalog[20], SeasonTeams, "One too many").Error!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ACorrectionNeedsAReason(string reason)
    {
        var roster = NewRoster();
        roster.SaveDraft(Nine(0), SeasonTeams, true);

        Assert.Equal("roster.reason", roster.CorrectByCommissioner(catalog[0], catalog[20], SeasonTeams, reason).Error!.Code);
        Assert.Contains(catalog[0], roster.Teams);
    }

    [Fact]
    public void AnInvalidCorrectionChangesNothing()
    {
        var roster = NewRoster();
        roster.SaveDraft(Nine(0), SeasonTeams, true);

        Assert.Equal("roster.correction.invalid", roster.CorrectByCommissioner(catalog[20], catalog[21], SeasonTeams, "x").Error!.Code);
        Assert.Equal("roster.duplicate", roster.CorrectByCommissioner(catalog[0], catalog[1], SeasonTeams, "x").Error!.Code);
        Assert.Equal("roster.team", roster.CorrectByCommissioner(catalog[0], new TeamId(Guid.NewGuid()), SeasonTeams, "x").Error!.Code);
        Assert.Equal(Nine(0).ToHashSet(), roster.Teams.ToHashSet());
        Assert.Equal(1, roster.Revision);
    }

    [Fact]
    public void ARosterIsReadyForWeeklyPicksOnlyWithExactlyNineTeams()
    {
        var roster = NewRoster();
        Assert.False(roster.IsComplete);
        roster.CorrectByCommissioner(null, catalog[0], SeasonTeams, "r");
        Assert.False(roster.IsComplete);
        roster.SaveDraft(Nine(0), SeasonTeams, true);
        Assert.True(roster.IsComplete);
    }
}
