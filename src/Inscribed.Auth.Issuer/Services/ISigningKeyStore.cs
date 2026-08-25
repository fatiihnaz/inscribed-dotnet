using System.Text.Json.Serialization;
using Inscribed.Auth.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Issuer.Services;

public interface ISigningKeyStore : ITokenValidationKeySource
{
    SigningCredentials GetActiveSigningCredentials();

    IEnumerable<SecurityKey> GetValidationKeys(string? kid = null);

    IReadOnlyList<JwksKey> GetPublicJwks();

    string Rotate();
}

public sealed record JwksKey(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("alg")] string Alg,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("n")] string N,
    [property: JsonPropertyName("e")] string E
);