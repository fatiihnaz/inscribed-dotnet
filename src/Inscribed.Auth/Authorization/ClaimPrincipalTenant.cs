using System.Security.Claims;
using Inscribed.Application.Contracts.Identity;
using Inscribed.Auth.Options;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth.Authorization;

internal sealed class ClaimPrincipalTenant : IPrincipalTenant
{
    private readonly AuthOptions _options;

    public ClaimPrincipalTenant(IOptions<AuthOptions> options) => _options = options.Value;

    public string? Resolve(ClaimsPrincipal user) => user.FindFirst(_options.TenantClaim)?.Value;
}
