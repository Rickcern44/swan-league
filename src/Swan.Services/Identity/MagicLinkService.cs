using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Core.Integrations;
using Swan.Data;

namespace Swan.Services.Identity;

public sealed record RedeemedUser(UserId UserId, string Name);
public sealed record CurrentUser(UserId UserId, string Name, string Role);

public sealed class MagicLinkService(SwanDbContext database, IEmailSender emailSender, IClock clock)
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> Requests = new();

    public async Task RequestAsync(Email email, string ipAddress, Uri linkBase, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var rateKey = $"{email.Value}:{ipAddress}";
        if (Requests.TryGetValue(rateKey, out var requestedAt) && requestedAt.AddMinutes(1) > now) return;
        Requests[rateKey] = now;

        var user = await database.Users.SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (user is null || !user.IsActive) return;

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.IssueLoginToken(Hash(rawToken), now);
        await database.SaveChangesAsync(cancellationToken);
        var link = new Uri(linkBase, $"?token={rawToken}");
        await emailSender.SendAsync(new EmailMessage(user.Email.Value, "Your Swan sign-in link", link.ToString(), false), cancellationToken);
    }

    public async Task<Result<RedeemedUser>> RedeemAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(rawToken);
        var user = await database.Users.SingleOrDefaultAsync(candidate => candidate.LoginTokens.Any(token => token.Value == hash), cancellationToken);
        if (user is null || !user.IsActive)
            return Result<RedeemedUser>.Failure("sign-in.invalid", "The sign-in link is invalid.");

        var redeemed = user.RedeemLoginToken(hash, clock.UtcNow);
        if (!redeemed.IsSuccess)
            return Result<RedeemedUser>.Failure(redeemed.Error!.Code, redeemed.Error.Message);

        await database.SaveChangesAsync(cancellationToken);
        return Result<RedeemedUser>.Success(new RedeemedUser(user.Id, user.Name));
    }

    private static string Hash(string value) => TokenHash.Of(value);
}

// One-way hash used for every stored credential: a magic-link token and a session credential are never kept, only this.
internal static class TokenHash
{
    public static string Of(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record IssuedSession(string Credential, DateTimeOffset ExpiresAt);

// A session is a random 256-bit credential held in the browser's cookie. The database keeps only its hash, so reading it does not let anyone sign in,
// and a session can be revoked or expire on the server regardless of what the browser still holds.
public sealed class SessionService(SwanDbContext database, IClock clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public async Task<IssuedSession> CreateAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = clock.UtcNow;
        var session = new UserSession(new SessionId(Guid.NewGuid()), userId, TokenHash.Of(credential), now, now.Add(Lifetime));
        database.Sessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);
        return new IssuedSession(credential, session.ExpiresAt);
    }

    // Only a credential this service issued (64 uppercase hex characters) can match; anything else, including a user id, is refused without a lookup.
    public async Task<CurrentUser?> AuthenticateAsync(string? credential, CancellationToken cancellationToken = default)
    {
        if (!LooksLikeCredential(credential)) return null;
        var hash = TokenHash.Of(credential!);
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (session is null || !session.IsActive(clock.UtcNow)) return null;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == session.UserId, cancellationToken);
        return user is null || !user.IsActive ? null : new CurrentUser(user.Id, user.Name, user.Role.ToString());
    }

    public async Task RevokeAsync(string? credential, CancellationToken cancellationToken = default)
    {
        if (!LooksLikeCredential(credential)) return;
        var hash = TokenHash.Of(credential!);
        var session = await database.Sessions.SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (session is null) return;
        session.Revoke(clock.UtcNow);
        await database.SaveChangesAsync(cancellationToken);
    }

    private static bool LooksLikeCredential(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
