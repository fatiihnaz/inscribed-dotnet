using Microsoft.AspNetCore.Http;

namespace Inscribed.Auth.Authentication;

public static class InscribedAuthSchemes
{
    public const string PolicyScheme = "InscribedAuth";
}

public interface IAuthenticationSchemeSelector
{
    string? Select(HttpRequest request);
}
