using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Platform;
using Swan.Core.Schedule;
using Swan.Data;

namespace Swan.Services.Platform;

public sealed record AuditFilter(UserId? Actor = null, string? Action = null, DateTimeOffset? From = null, DateTimeOffset? To = null, WeekId? WeekId = null);

public interface IAuditService
{
    Task AppendAsync(UserId actor, string action, string resourceId, object? before = null, object? after = null, string? reason = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditFilter filter, CancellationToken cancellationToken = default);
}

public sealed class AuditService(SwanDbContext database, IClock clock) : IAuditService
{
    public async Task AppendAsync(UserId actor, string action, string resourceId, object? before = null, object? after = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        var details = JsonSerializer.Serialize(new { before, after, reason });
        database.AuditEntries.Add(new AuditEntry(new AuditEntryId(Guid.NewGuid()), actor, action, clock.UtcNow, resourceId, details));
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditFilter filter, CancellationToken cancellationToken = default)
    {
        var entries = database.AuditEntries.AsNoTracking().AsQueryable();
        if (filter.Actor is { } actor) entries = entries.Where(entry => entry.UserId == actor);
        if (!string.IsNullOrWhiteSpace(filter.Action)) entries = entries.Where(entry => entry.Action == filter.Action);
        if (filter.From is { } from) entries = entries.Where(entry => entry.At >= from);
        if (filter.To is { } to) entries = entries.Where(entry => entry.At <= to);
        if (filter.WeekId is { } weekId) entries = entries.Where(entry => entry.ResourceId == weekId.Value.ToString());
        return await entries.OrderByDescending(entry => entry.At).ToListAsync(cancellationToken);
    }
}
