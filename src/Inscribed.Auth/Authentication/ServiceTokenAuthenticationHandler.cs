using System.Security.Claims;
using System.Text.Encodings.Web;
using Inscribed.Auth.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth.Authentication;

internal static class InscribedAuthSchemes
{
    public const string PolicyScheme = "InscribedAuth";
    public const string ServiceToken = "ServiceToken";
}

internal static class ServiceTokenLocator
{
    public static string? Locate(HttpRequest request)
    {
        string? headerKey = request.Headers["X-Service-Key"];
        if (!string.IsNullOrWhiteSpace(headerKey))
        {
            return headerKey;
        }

        string? authorization = request.Headers.Authorization;
        const string bearerPrefix = "Bearer ";
        if (authorization is not null && authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization[bearerPrefix.Length..].Trim();
            if (token.StartsWith(ServiceKeyFormat.Prefix, StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }
}

internal sealed class ServiceTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IServiceKeyService _serviceKeys;

    public ServiceTokenAuthenticationHandler(
        IServiceKeyService serviceKeys,
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
        _serviceKeys = serviceKeys;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var rawKey = ServiceTokenLocator.Locate(Request);
        if (rawKey is null)
        {
            return AuthenticateResult.NoResult();
        }

        var key = await _serviceKeys.ValidateAsync(rawKey, Context.RequestAborted);
        if (key is null)
        {
            return AuthenticateResult.Fail("Invalid, expired or revoked service key.");
        }

        var claims = new List<Claim>
        {
            new("sub", $"service:{key.Id}"),
            new("azp", key.ClientKey),
            new("preferred_username", key.Name),
        };
        claims.AddRange(key.Roles.Select(role => new Claim("roles", role)));

        var identity = new ClaimsIdentity(claims, Scheme.Name, "preferred_username", "roles");
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
