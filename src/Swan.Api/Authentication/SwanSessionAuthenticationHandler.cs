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
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.Cookies["swan_session"], out var id)) return AuthenticateResult.NoResult();

        var sessions = Context.RequestServices.GetRequiredService<SessionService>();
        var user = await sessions.GetAsync(new UserId(id), Context.RequestAborted);
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
