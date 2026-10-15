using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Services.Identity;

namespace Swan.Api.Authentication;

public sealed class SwanSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string CookieName = "swan_session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // The cookie holds an opaque credential, never a user id; it is accepted only if it matches an active, unexpired, unrevoked session of an active user.
        var credential = Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(credential)) return AuthenticateResult.NoResult();

        var sessions = Context.RequestServices.GetRequiredService<SessionService>();
        var user = await sessions.AuthenticateAsync(credential, Context.RequestAborted);
        if (user is null) return AuthenticateResult.Fail("The session is invalid.");

        var identity = new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.Value.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role)
        }, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
