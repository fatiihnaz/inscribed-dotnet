using Inscribed.Auth.Options;
using Inscribed.Auth.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Authentication;

internal sealed class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly ISigningKeyStore _keys;
    private readonly AuthOptions _options;

    public ConfigureJwtBearerOptions(ISigningKeyStore keys, IOptions<AuthOptions> options)
    {
        _keys = keys;
        _options = options.Value;
    }

    public void Configure(JwtBearerOptions options)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "preferred_username",
            RoleClaimType = "roles",
            IssuerSigningKeyResolver = (_, _, kid, _) => _keys.GetValidationKeys(kid),
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