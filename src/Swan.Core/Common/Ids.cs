namespace Swan.Core.Common;

public readonly record struct UserId(Guid Value);
public readonly record struct SeasonId(Guid Value);
public readonly record struct MembershipId(Guid Value);
public readonly record struct AuditEntryId(Guid Value);
public readonly record struct ScheduledJobId(Guid Value);
public readonly record struct TeamId(Guid Value);
public readonly record struct SeasonTeamId(Guid Value);
public readonly record struct WeekId(Guid Value);
public readonly record struct GameId(Guid Value);
public readonly record struct RosterId(Guid Value);
public readonly record struct BallotId(Guid Value);
public readonly record struct WeekScoreId(Guid Value);
public readonly record struct LedgerId(Guid Value);
public readonly record struct PoolSettlementId(Guid Value);
public readonly record struct AnnouncementId(Guid Value);
public readonly record struct NotificationId(Guid Value);
public readonly record struct PushSubscriptionId(Guid Value);
