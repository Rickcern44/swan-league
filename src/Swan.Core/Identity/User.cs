using Swan.Core.Common;

namespace Swan.Core.Identity;

public sealed record LoginToken(string Value, DateTimeOffset ExpiresAt, bool Used = false)
{
    public Result<LoginToken> Use(DateTimeOffset now) => Used || now >= ExpiresAt
        ? Result<LoginToken>.Failure("login-token.invalid", "Token is used or expired.")
        : Result<LoginToken>.Success(this with { Used = true });
}

public sealed class User(UserId id, Email email, string name) : AggregateRoot
{
    private readonly List<LoginToken> tokens = [];
    public UserId Id { get; } = id;
    public Email Email { get; } = email;
    public string Name { get; private set; } = name;
    public IReadOnlyCollection<LoginToken> LoginTokens => tokens.AsReadOnly();
    public LoginToken IssueLoginToken(string value, DateTimeOffset now) => new(value, now.AddMinutes(15));
}
