using Swan.Core;

namespace Swan.Services.Development;

// Development-only clock control is registered by the API only in Development. Services still
// depend on IClock, so production always remains on SystemClock.
public sealed class DevelopmentClock(DateTimeOffset initial) : IClock
{
    private DateTimeOffset utcNow = initial.ToUniversalTime();

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (this) return utcNow;
        }
    }

    public DateTimeOffset Set(DateTimeOffset value)
    {
        lock (this) return utcNow = value.ToUniversalTime();
    }

    public DateTimeOffset Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by), "Development time can only move forward.");
        lock (this) return utcNow = utcNow.Add(by);
    }
}
