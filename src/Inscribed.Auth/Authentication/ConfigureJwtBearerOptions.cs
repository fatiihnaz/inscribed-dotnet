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

    public ConfigureJwtBearerOptions(IOptions<AuthOptions> options, ITokenValidationKeySource? keys)
    {
        _options = options.Value;
        _keys = keys;
    }

    public void Configure(JwtBearerOptions options)
    {
        var keys = _keys ?? throw new InvalidOperationException(
            "No ITokenValidationKeySource is registered, so incoming tokens cannot be verified. "
            + "Register the bundled issuer with AddInscribedAuthIssuer().");

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            NameClaimType = "name",
            RoleClaimType = CapabilityCatalog.RolesClaim,
            IssuerSigningKeyResolver = (_, _, kid, _) => keys.Resolve(kid),
        };
    }

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name is null || name == JwtBearerDefaults.AuthenticationScheme)
        {
            Configure(options);
        }
    }
}
