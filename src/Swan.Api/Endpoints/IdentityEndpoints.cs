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
            var baseUri = new Uri($"{context.Request.Scheme}://{context.Request.Host}/api/sign-in-links/redeem");
            await magicLinks.RequestAsync(email.Value!, context.Connection.RemoteIpAddress?.ToString() ?? "unknown", baseUri, cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<IResult> RedeemSignInLink(
        string token,
        HttpContext context,
        MagicLinkService magicLinks,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var redeemed = await magicLinks.RedeemAsync(token, cancellationToken);
        if (!redeemed.IsSuccess) return Results.Unauthorized();

        context.Response.Cookies.Append("swan_session", redeemed.Value!.UserId.Value.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = clock.UtcNow.AddDays(30)
        });
        return Results.Ok();
    }

    private static async Task<IResult> GetCurrentUser(HttpContext context, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Request.Cookies["swan_session"], out var value)) return Results.Unauthorized();
        var sessions = services.GetRequiredService<SessionService>();
        var user = await sessions.GetAsync(new UserId(value), cancellationToken);
        return user is null ? Results.Unauthorized() : Results.Ok(new { id = user.UserId.Value, user.Name, user.Role });
    }

    private sealed record RequestSignInLinkRequest(string Email);
}
