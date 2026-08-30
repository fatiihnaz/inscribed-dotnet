using Inscribed.Application.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Inscribed.Auth.Authorization;

public static class TenantScopedAdmin
{
    public const string RouteKey = "key";

    public static RouteGroupBuilder RequireOwnTenant(this RouteGroupBuilder builder)
        => builder.AddEndpointFilter(EnforceAsync);

    public static RouteHandlerBuilder RequireOwnTenant(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter(EnforceAsync);

    private static async ValueTask<object?> EnforceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (http.User.IsInRole(CapabilityCatalog.ServiceAdmin))
        {
            return await next(context);
        }

        var target = http.Request.RouteValues.TryGetValue(RouteKey, out var value) ? value?.ToString() : null;
        var tenant = http.RequestServices.GetRequiredService<IPrincipalTenant>().Resolve(http.User);

        if (target is null || tenant is null || !string.Equals(target, tenant, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"'{CapabilityCatalog.ClientAdmin}' administers only the client it was granted on"
                + $"{(tenant is null ? string.Empty : $" ('{tenant}')")}. "
                + $"Administering '{target}' needs '{CapabilityCatalog.ServiceAdmin}'.");
        }

        return await next(context);
    }
}
