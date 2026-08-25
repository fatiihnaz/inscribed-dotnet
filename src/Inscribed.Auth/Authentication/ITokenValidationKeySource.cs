using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Authentication;

public interface ITokenValidationKeySource
{
    IEnumerable<SecurityKey> Resolve(string? kid);
}
