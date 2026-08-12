using System.Security.Claims;

namespace Inscribed.Application.Contracts.Identity;

public interface IAdministratorPolicy
{
    bool IsAdministrator(ClaimsPrincipal user);
}
