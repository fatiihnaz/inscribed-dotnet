using System.Security.Claims;

namespace Inscribed.Api.Authentication;

public static class UserPrincipalExtensions
{
    public static string? GetUserSub(this ClaimsPrincipal principal) => principal.FindFirst("sub")?.Value;
}