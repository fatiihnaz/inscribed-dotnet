using System.Security.Claims;
using System.Text.Json;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Authentication;

internal sealed class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly ITokenValidationKeySource? _keys;
    private readonly AuthOptions _options;
    private readonly Dictionary<string, string> _roleMap;

    public ConfigureJwtBearerOptions(IOptions<AuthOptions> options, ITokenValidationKeySource? keys)
    {
        _options = options.Value;
        _keys = keys;
        _roleMap = new Dictionary<string, string>(_options.RoleMap, StringComparer.OrdinalIgnoreCase);
    }

    public void Configure(JwtBearerOptions options)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            NameClaimType = "name",
            RoleClaimType = CapabilityCatalog.RolesClaim,
        };

        if (_options.Mode is AuthMode.External)
        {
            // ValidIssuer stays unset on purpose: the handler reads iss and the signing keys
            // from the discovery document, and a proxied Keycloak whose iss differs from the
            // configured base URL would otherwise fail every token.
            options.Authority = _options.Authority;
            options.RequireHttpsMetadata = _options.RequireHttpsMetadata;
        }
        else
        {
            var keys = _keys ?? throw new InvalidOperationException(
                "Auth:Mode is BuiltIn but no ITokenValidationKeySource is registered, so incoming tokens cannot be verified. "
                + "Register the bundled issuer with AddInscribedAuthIssuer(), or set Auth:Mode to External.");

            options.TokenValidationParameters.ValidIssuer = _options.Issuer;
            options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) => keys.Resolve(kid);
        }

        if (!string.Equals(_options.RolesClaim, CapabilityCatalog.RolesClaim, StringComparison.Ordinal) || _roleMap.Count > 0)
        {
            options.Events = new JwtBearerEvents { OnTokenValidated = NormalizeRolesAsync };
        }
    }

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name is null || name == JwtBearerDefaults.AuthenticationScheme)
        {
            Configure(options);
        }
    }

    private Task NormalizeRolesAsync(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return Task.CompletedTask;
        }

        var seen = identity.FindAll(CapabilityCatalog.RolesClaim)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var role in ReadSourceRoles(identity))
        {
            var mapped = _roleMap.TryGetValue(role, out var replacement) ? replacement : role;

            if (seen.Add(mapped))
            {
                identity.AddClaim(new Claim(CapabilityCatalog.RolesClaim, mapped));
            }
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<string> ReadSourceRoles(ClaimsIdentity identity)
    {
        var separator = _options.RolesClaim.IndexOf('.');

        if (separator < 0)
        {
            return [.. identity.FindAll(_options.RolesClaim).Select(claim => claim.Value)];
        }

        // Keycloak nests realm roles under realm_access.roles, which arrives as one JSON-valued
        // claim rather than a repeated string claim.
        var container = identity.FindFirst(_options.RolesClaim[..separator]);

        if (container is null)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(container.Value);
            var element = document.RootElement;

            foreach (var segment in _options.RolesClaim[(separator + 1)..].Split('.'))
            {
                if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(segment, out var child))
                {
                    return [];
                }

                element = child;
            }

            return element.ValueKind is JsonValueKind.Array
                ? [.. element.EnumerateArray().Where(item => item.ValueKind is JsonValueKind.String).Select(item => item.GetString()!)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
