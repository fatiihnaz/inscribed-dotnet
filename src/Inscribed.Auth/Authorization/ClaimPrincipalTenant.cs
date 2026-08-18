using System.Security.Claims;
using Inscribed.Application.Contracts.Identity;

namespace Inscribed.Auth.Authorization;

internal sealed class ClaimPrincipalTenant : IPrincipalTenant
{
    public string? Resolve(ClaimsPrincipal user) => user.FindFirst(CapabilityCatalog.TenantClaim)?.Value;
}
