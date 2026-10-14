using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Core.League;
using Swan.Core.Platform;
using Xunit;

namespace Swan.Core.Tests;

public sealed class IdentityLeaguePlatformTests
{
    [Fact]
    public void LoginTokenIsSingleUseAndExpiresAfterFifteenMinutes()
    {
        var now = DateTimeOffset.UtcNow;
        var token = new User(new UserId(Guid.NewGuid()), Email.Create("coach@example.com").Value!, "Coach")
            .IssueLoginToken("token", now);

        var usedToken = token.Use(now.AddMinutes(14)).Value!;
        Assert.False(usedToken.Use(now).IsSuccess);
        Assert.False(token.Use(now.AddMinutes(15)).IsSuccess);
    }

    [Fact]
    public void SeasonActivationRequiresValidStructuralSettingsAndRaisesEvent()
    {
        var buyIn = Money.Create(100, "USD").Value!;
        var season = new Season(new SeasonId(Guid.NewGuid()), new SeasonSettings(buyIn, 3), [new PoolDefinition("Main", buyIn)]);

        Assert.True(season.ActivateWithDogs().IsSuccess);
        Assert.Equal(SeasonState.Live, season.State);
        Assert.Single(season.DomainEvents.OfType<SeasonActivated>());
        Assert.False(season.UpdateSettings(new SeasonSettings(buyIn, 3)).IsSuccess);
    }

    [Fact]
    public void MembershipInvitationRaisesEvent()
    {
        var membership = new Membership(new MembershipId(Guid.NewGuid()), new UserId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), MembershipRole.Coach);
        membership.Invite();
        Assert.Single(membership.DomainEvents.OfType<MembershipInvited>());
    }

    [Fact]
    public void ScheduledJobLeaseAllowsOnlyOneNode()
    {
        var job = new ScheduledJob(new ScheduledJobId(Guid.NewGuid()), "reminders", DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        Assert.True(job.TryClaim("node-a", now, TimeSpan.FromMinutes(1)));
        Assert.False(job.TryClaim("node-b", now, TimeSpan.FromMinutes(1)));
    }
}
