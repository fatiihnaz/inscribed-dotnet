using System.Security.Claims;

namespace Inscribed.Application.Contracts.Identity;

public interface IPrincipalTenant
{
    string? Resolve(ClaimsPrincipal user);
}
