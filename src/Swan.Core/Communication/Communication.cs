using Swan.Core.Common;

namespace Swan.Core.Communication;

public enum NotificationCategory { Invitations, Picks, Reminders, Announcements }

public sealed class Announcement(AnnouncementId id, string content, DateTimeOffset scheduledFor)
{
    public AnnouncementId Id { get; } = id;
    public string Content { get; } = content;
    public DateTimeOffset ScheduledFor { get; } = scheduledFor;
    public bool IsSent { get; private set; }

    public static Result<Announcement> Schedule(AnnouncementId id, string content, DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(content) || scheduledFor <= now)
            return Result<Announcement>.Failure("announcement.schedule.invalid", "Announcements require content and a future schedule.");

        return Result<Announcement>.Success(new Announcement(id, content, scheduledFor));
    }

    public Result<Announcement> Send(DateTimeOffset now)
    {
        if (IsSent)
            return Result<Announcement>.Success(this);
        if (now < ScheduledFor)
            return Result<Announcement>.Failure("announcement.not-due", "The announcement is not due yet.");

        IsSent = true;
        return Result<Announcement>.Success(this);
    }
}

public sealed class Notification(NotificationId id, MembershipId recipientId, string eventKey, string content)
{
    public NotificationId Id { get; } = id;
    public MembershipId RecipientId { get; } = recipientId;
    public string EventKey { get; } = eventKey;
    public string Content { get; } = content;
    public bool IsRead { get; private set; }

    public void MarkRead() => IsRead = true;
    public static bool HasUniqueRecipientEventKeys(IEnumerable<Notification> notifications) =>
        notifications.Select(notification => (notification.RecipientId, notification.EventKey)).Distinct().Count() == notifications.Count();
}

public sealed class NotificationPreferences(MembershipId membershipId)
{
    private readonly HashSet<NotificationCategory> mutedCategories = [];

    public MembershipId MembershipId { get; } = membershipId;
    public IReadOnlySet<NotificationCategory> MutedCategories => mutedCategories;

    public Result<NotificationPreferences> Mute(NotificationCategory category)
    {
        if (category == NotificationCategory.Invitations)
            return Result<NotificationPreferences>.Failure("notification-preferences.essential", "Member invitations cannot be muted.");

        mutedCategories.Add(category);
        return Result<NotificationPreferences>.Success(this);
    }

    public void Unmute(NotificationCategory category) => mutedCategories.Remove(category);
}

public sealed record PushSubscription(PushSubscriptionId Id, MembershipId MembershipId, string Endpoint, string P256Dh, string Auth);
