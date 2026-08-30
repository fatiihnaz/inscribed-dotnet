using Inscribed.Application.Contracts.Identity;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth.Endpoints;

public static class WhoAmIEndpoint
{
    public static IEndpointRouteBuilder MapWhoAmIEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/whoami", (HttpContext context, IPrincipalTenant tenants, IOptions<AuthOptions> options) =>
        {
            var user = context.User;
            var settings = options.Value;

            return Results.Ok(new
            {
                mode = settings.Mode.ToString(),
                subject = user.FindFirst("sub")?.Value,
                tenant = tenants.Resolve(user),
                tenantClaim = settings.TenantClaim,
                capabilities = user.FindAll(CapabilityCatalog.RolesClaim).Select(claim => claim.Value).ToArray(),
                roleClaimType = (user.Identity as System.Security.Claims.ClaimsIdentity)?.RoleClaimType,
                administrator = user.IsInRole(CapabilityCatalog.ClientAdmin) || user.IsInRole(CapabilityCatalog.ServiceAdmin),
                name = user.Identity?.Name,
                email = user.FindFirst("email")?.Value,
                claims = user.Claims
                    .GroupBy(claim => claim.Type, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Select(claim => claim.Value).ToArray(), StringComparer.Ordinal),
            });
        }).RequireAuthorization();

        return app;
    }
}
