using System.Security.Claims;

namespace Inscribed.Api.Authentication;

public static class UserPrincipalExtensions
{
    public static string? GetClientId(this ClaimsPrincipal principal) => principal.FindFirst("azp")?.Value;

    public static string? GetUserSub(this ClaimsPrincipal principal) => principal.FindFirst("sub")?.Value;
}