using System.Security.Claims;
using Inscribed.Application.Contracts.Identity;

namespace Inscribed.Auth.Authorization;

internal sealed class CapabilityAdministratorPolicy : IAdministratorPolicy
{
    public bool IsAdministrator(ClaimsPrincipal user) => user.IsInRole(CapabilityCatalog.TenantAdmin);
}
