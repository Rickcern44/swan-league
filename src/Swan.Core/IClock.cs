namespace Swan.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
