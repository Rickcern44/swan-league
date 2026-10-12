using Swan.Core.Communication;
using Swan.Core.Common;
using Swan.Core.Finance;
using Xunit;

namespace Swan.Core.Tests;

public sealed class MoneyCommunicationTests
{
    [Fact]
    public void LedgerItemMovesOnlyForwardAndRaisesFineEvent()
    {
        var ledger = new Ledger(new LedgerId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()));
        var amount = Money.Create(25m, "usd").Value!;
        var item = ledger.AssessFine(amount, "Late pick", new MembershipId(Guid.NewGuid()), new MembershipId(Guid.NewGuid())).Value!;

        Assert.True(item.Confirm().IsSuccess);
        Assert.True(item.Pay().IsSuccess);
        Assert.False(item.Waive().IsSuccess);
        Assert.Contains(ledger.DomainEvents, domainEvent => domainEvent is FineAssessed);
        Assert.False(ledger.AssessFine(Money.Create(0m, "USD").Value!, "Nope", default, default).IsSuccess);
    }

    [Fact]
    public void PoolSettlementRequiresTheExactPotAndCanOnlySettleOnce()
    {
        var settlement = new PoolSettlement(new PoolSettlementId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), Money.Create(100m, "USD").Value!);
        var placing = new Placing(new MembershipId(Guid.NewGuid()), 1, Money.Create(100m, "USD").Value!);

        Assert.True(settlement.Settle([placing]).IsSuccess);
        Assert.False(settlement.Settle([placing]).IsSuccess);
        Assert.Contains(settlement.DomainEvents, domainEvent => domainEvent is PoolSettled);
    }

    [Fact]
    public void CommunicationEnforcesDeliveryAndPreferenceInvariants()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(Announcement.Schedule(new AnnouncementId(Guid.NewGuid()), "Update", now, now).IsSuccess);
        var announcement = Announcement.Schedule(new AnnouncementId(Guid.NewGuid()), "Update", now.AddMinutes(1), now).Value!;
        Assert.False(announcement.Send(now).IsSuccess);
        Assert.True(announcement.Send(now.AddMinutes(1)).IsSuccess);
        Assert.True(announcement.Send(now.AddMinutes(2)).IsSuccess);

        var member = new MembershipId(Guid.NewGuid());
        var preferences = new NotificationPreferences(member);
        Assert.False(preferences.Mute(NotificationCategory.Invitations).IsSuccess);
        Assert.True(preferences.Mute(NotificationCategory.Reminders).IsSuccess);

        var notifications = new[] { new Notification(new NotificationId(Guid.NewGuid()), member, "game-1", "Ready") };
        Assert.True(Notification.HasUniqueRecipientEventKeys(notifications));
        var subscription = new PushSubscription(new PushSubscriptionId(Guid.NewGuid()), member, "https://push.example/sub", "p256dh", "auth");
        Assert.Equal("auth", subscription.Auth);
    }
}
