using Inscribed.Auth.Authorization;
using Inscribed.Auth.Issuer.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Issuer.Services;

internal sealed class JwtIssuer : IJwtIssuer
{
    private readonly ISigningKeyStore _keys;
    private readonly AuthIssuerOptions _options;

    public JwtIssuer(ISigningKeyStore keys, IOptions<AuthIssuerOptions> options)
    {
        _keys = keys;
        _options = options.Value;
    }

    public AccessToken Issue(string subject, string clientKey, IReadOnlyList<string> roles, string displayName, string email)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = _keys.GetActiveSigningCredentials(),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["azp"] = clientKey,
                ["name"] = displayName,
                ["email"] = email,
                [CapabilityCatalog.RolesClaim] = roles.ToArray(),
            },
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expires);
    }
}