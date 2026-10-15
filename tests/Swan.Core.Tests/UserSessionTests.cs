using Swan.Core.Common;
using Swan.Core.Identity;
using Xunit;

namespace Swan.Core.Tests;

public sealed class UserSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static UserSession Session(string hash = "A1B2") => new(new SessionId(Guid.NewGuid()), new UserId(Guid.NewGuid()), hash, Now, Now.AddDays(30));

    [Fact]
    public void ASessionIsActiveFromCreationUntilItExpires()
    {
        var session = Session();

        Assert.True(session.IsActive(Now));
        Assert.True(session.IsActive(Now.AddDays(30).AddTicks(-1)));
        Assert.False(session.IsActive(Now.AddDays(30)));
        Assert.False(session.IsActive(Now.AddDays(31)));
    }

    [Fact]
    public void RevokingEndsASessionAtOnceAndKeepsTheFirstRevocationTime()
    {
        var session = Session();

        session.Revoke(Now.AddHours(1));
        session.Revoke(Now.AddHours(5));

        Assert.False(session.IsActive(Now.AddHours(2)));
        Assert.Equal(Now.AddHours(1), session.RevokedAt);
    }

    [Fact]
    public void ASessionNeverHoldsAnythingButTheHashItWasGiven()
    {
        var session = Session("HASHONLY");

        Assert.Equal("HASHONLY", session.TokenHash);
        Assert.DoesNotContain(typeof(UserSession).GetProperties(), property => property.Name is "Token" or "Value" or "Secret");
    }
}
