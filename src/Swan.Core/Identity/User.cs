using Swan.Core.Common;

namespace Swan.Core.Identity;

public sealed class LoginToken(string value, DateTimeOffset expiresAt)
{
    public string Value { get; } = value;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public bool Used { get; private set; }

    public Result<LoginToken> Use(DateTimeOffset now)
    {
        if (Used || now >= ExpiresAt)
            return Result<LoginToken>.Failure("login-token.invalid", "Token is used or expired.");

        Used = true;
        return Result<LoginToken>.Success(this);
    }
}

public sealed class User(UserId id, Email email, string name) : AggregateRoot
{
    private readonly List<LoginToken> tokens = [];
    public UserId Id { get; } = id;
    public Email Email { get; } = email;
    public string Name { get; private set; } = name;
    public bool IsActive { get; private set; } = true;
    public IReadOnlyCollection<LoginToken> LoginTokens => tokens;
    public LoginToken IssueLoginToken(string value, DateTimeOffset now)
    {
        var token = new LoginToken(value, now.AddMinutes(15));
        tokens.Add(token);
        return token;
    }

    public Result<LoginToken> RedeemLoginToken(string value, DateTimeOffset now)
    {
        var token = tokens.SingleOrDefault(candidate => candidate.Value == value);
        return token is null
            ? Result<LoginToken>.Failure("login-token.invalid", "Token is invalid.")
            : token.Use(now);
    }

    public void Deactivate() => IsActive = false;
}
