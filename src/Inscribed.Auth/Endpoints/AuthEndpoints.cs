using Inscribed.Auth.Options;
using Inscribed.Auth.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapInscribedAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/jwks.json", (HttpContext context, ISigningKeyStore keys) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Json(new { keys = keys.GetPublicJwks() });
        });

        var group = app.MapGroup("/auth");

        group.MapGet("/login", async (string? clientKey, string? redirectUri, IGoogleLoginService login, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(clientKey) || string.IsNullOrWhiteSpace(redirectUri))
            {
                return Results.BadRequest("clientKey and redirectUri are required.");
            }

            var authorizationUrl = await login.StartAsync(clientKey, redirectUri, ct);
            return authorizationUrl is null
                ? Results.BadRequest("Unknown client or redirect origin not allowed.")
                : Results.Redirect(authorizationUrl);
        });

        group.MapGet("/google/callback", async (string? state, string? code, string? error, HttpContext context, IGoogleLoginService login, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
            {
                return Results.BadRequest("Login was cancelled or the callback is malformed.");
            }

            var completion = await login.CompleteAsync(state, code, ct);
            if (completion is null)
            {
                return Results.Unauthorized();
            }

            AppendRefreshCookie(context, options.Value, completion.RefreshToken, completion.RefreshExpiresAtUtc);
            return Results.Redirect(completion.RedirectUri);
        });

        group.MapPost("/refresh", async (HttpContext context, IRefreshTokenService refresh, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            var rawToken = context.Request.Cookies[options.Value.Cookie.Name];
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return Results.Unauthorized();
            }

            var result = await refresh.RefreshAsync(rawToken, ct);
            if (result is null)
            {
                DeleteRefreshCookie(context, options.Value);
                return Results.Unauthorized();
            }

            AppendRefreshCookie(context, options.Value, result.NewRefreshToken, result.RefreshExpiresAtUtc);
            return Results.Ok(new { accessToken = result.AccessToken.Token, expiresAtUtc = result.AccessToken.ExpiresAtUtc });
        });

        group.MapPost("/logout", async (HttpContext context, IRefreshTokenService refresh, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            var rawToken = context.Request.Cookies[options.Value.Cookie.Name];
            if (!string.IsNullOrWhiteSpace(rawToken))
            {
                await refresh.RevokeAsync(rawToken, ct);
            }

            DeleteRefreshCookie(context, options.Value);
            return Results.NoContent();
        });

        app.MapInscribedAdminEndpoints();

        return app;
    }

    private static void AppendRefreshCookie(HttpContext context, AuthOptions options, string token, DateTime expiresAtUtc)
    {
        context.Response.Cookies.Append(options.Cookie.Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.Cookie.Secure,
            SameSite = options.Cookie.SameSite,
            Path = "/auth",
            Expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero),
            IsEssential = true,
        });
    }

    private static void DeleteRefreshCookie(HttpContext context, AuthOptions options)
    {
        context.Response.Cookies.Delete(options.Cookie.Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.Cookie.Secure,
            SameSite = options.Cookie.SameSite,
            Path = "/auth",
        });
    }
}
