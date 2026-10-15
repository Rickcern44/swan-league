using Swan.Core.Common;

namespace Swan.Core.Identity;

// A signed-in browser. Only a one-way hash of the credential is ever kept; the credential itself lives in the cookie and nowhere else.
public sealed class UserSession(SessionId id, UserId userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
{
    public SessionId Id { get; } = id;
    public UserId UserId { get; } = userId;
    public string TokenHash { get; } = tokenHash;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    // Revoking twice keeps the first time.
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
