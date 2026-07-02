using Inscribed.Auth.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Services;

internal sealed class JwtIssuer : IJwtIssuer
{
    private readonly ISigningKeyStore _keys;
    private readonly AuthOptions _options;

    public JwtIssuer(ISigningKeyStore keys, IOptions<AuthOptions> options)
    {
        _keys = keys;
        _options = options.Value;
    }

    public AccessToken Issue(string subject, string clientKey, IReadOnlyList<string> roles, string username)
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
                ["preferred_username"] = username,
                ["roles"] = roles.ToArray(),
            },
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expires);
    }
}