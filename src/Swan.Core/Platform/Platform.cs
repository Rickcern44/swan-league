using Swan.Core.Common;

namespace Swan.Core.Platform;

public sealed record AuditEntry(AuditEntryId Id, UserId UserId, string Action, DateTimeOffset At, string ResourceId, string Details);
public sealed class ScheduledJob(ScheduledJobId id, string key, DateTimeOffset nextRunAt)
{
    public ScheduledJobId Id { get; } = id;
    public string Key { get; } = key;
    public DateTimeOffset NextRunAt { get; private set; } = nextRunAt; 
    public string? LeaseNodeId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public bool TryClaim(string nodeId, DateTimeOffset now, TimeSpan lease) { if (LeaseExpiresAt > now && LeaseNodeId != nodeId) return false; LeaseNodeId = nodeId; LeaseExpiresAt = now.Add(lease); return true; }
}
