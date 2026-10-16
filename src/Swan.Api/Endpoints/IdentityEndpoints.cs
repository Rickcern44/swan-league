using System.Security.Claims;
using Swan.Api.Authentication;
using Swan.Core;
using Swan.Core.Common;
using Swan.Core.Identity;
using Swan.Services.Identity;

namespace Swan.Api.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sign-in-links", RequestSignInLink).AllowAnonymous();
        endpoints.MapGet("/api/sign-in-links/redeem", RedeemSignInLink).AllowAnonymous();
        endpoints.MapPost("/api/sign-out", SignOut).AllowAnonymous();
        endpoints.MapGet("/api/me", GetCurrentUser).RequireAuthorization("Coach");
        return endpoints;
    }

    private static async Task<IResult> RequestSignInLink(
        RequestSignInLinkRequest request,
        HttpContext context,
        MagicLinkService magicLinks,
        CancellationToken cancellationToken)
    {
        var email = Email.Create(request.Email);
        if (email.IsSuccess)
        {
            var baseUri = new Uri($"{context.Request.Scheme}://{context.Request.Host}/signin-callback");
            await magicLinks.RequestAsync(email.Value!, context.Connection.RemoteIpAddress?.ToString() ?? "unknown", baseUri, cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<IResult> RedeemSignInLink(
        string token,
        HttpContext context,
        MagicLinkService magicLinks,
        SessionService sessions,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var redeemed = await magicLinks.RedeemAsync(token, cancellationToken);
        if (!redeemed.IsSuccess) return Results.Unauthorized();

        // The cookie carries a fresh random credential, never the user id; only its hash is stored.
        var session = await sessions.CreateAsync(redeemed.Value!.UserId, cancellationToken);
        context.Response.Cookies.Append(SwanSessionAuthenticationHandler.CookieName, session.Credential, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Expires = session.ExpiresAt
        });
        return Results.Ok();
    }

    // Signing out ends the presented session on the server, so a copy of the cookie stops working too. It is safe to call with no session.
    private static async Task<IResult> SignOut(HttpContext context, SessionService sessions, IHostEnvironment environment, CancellationToken cancellationToken)
    {
        await sessions.RevokeAsync(context.Request.Cookies[SwanSessionAuthenticationHandler.CookieName], cancellationToken);
        context.Response.Cookies.Delete(SwanSessionAuthenticationHandler.CookieName, new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Lax });
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUser(ClaimsPrincipal principal, SessionService sessions, IHostEnvironment environment, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Results.Unauthorized();
        var role = principal.FindFirstValue(ClaimTypes.Role);
        var access = await sessions.AccessAsync(new UserId(id), role == nameof(UserRole.Admin), cancellationToken);
        return Results.Ok(new { id, Name = principal.FindFirstValue(ClaimTypes.Name), Role = role, SetupAccess = access.Setup, CommissionsLiveSeason = access.CommissionsLiveSeason, DevelopmentTools = environment.IsDevelopment() && role == nameof(UserRole.Admin) });
    }

    private sealed record RequestSignInLinkRequest(string Email);
}
