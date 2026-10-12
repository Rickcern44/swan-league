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

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class SessionService(SwanDbContext database)
{
    public async Task<CurrentUser?> GetAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var user = await database.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null || !user.IsActive) return null;
        return new CurrentUser(user.Id, user.Name, user.Role.ToString());
    }
}
