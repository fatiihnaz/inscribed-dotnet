using Inscribed.Application.Services;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Issuer.Services;

namespace Inscribed.Api.Endpoints;

public static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/clients");

        group.MapGet("/", async (IClientService clients, CancellationToken ct) =>
        {
            var all = await clients.ListAsync(ct);
            return Results.Ok(all);
        }).RequireAuthorization("ServiceAdmin");

        group.MapPost("/", async (CreateClientRequest request, IClientService clients, CancellationToken ct) =>
        {
            var client = await clients.CreateAsync(request.Key, request.Name, request.AllowedRedirectOrigins, ct);
            return Results.Created($"/admin/clients/{client.Key}", client);
        }).RequireAuthorization("ServiceAdmin");

        group.MapGet("/{key}", async (string key, IClientService clients, IServiceProvider services, CancellationToken ct) =>
        {
            var client = await clients.GetAsync(key, ct);

            if (services.GetService<IAdminService>() is not { } admin)
            {
                return Results.Ok(new
                {
                    client.Id,
                    client.Key,
                    client.Locales,
                    client.AllowAnonymousContentRead,
                    client.IsActive,
                    client.CreatedAt,
                });
            }

            var identity = await admin.GetClientAsync(key, ct);

            return Results.Ok(new
            {
                client.Id,
                client.Key,
                identity.Name,
                identity.AllowedRedirectOrigins,
                client.Locales,
                client.AllowAnonymousContentRead,
                client.IsActive,
                IdentityIsActive = identity.IsActive,
                identity.ServiceKeys,
                identity.ActiveServiceKeys,
                identity.Members,
                client.CreatedAt,
            });
        }).RequireAuthorization("ClientAdmin").RequireOwnTenant();

        group.MapPut("/{key}", async (string key, UpdateClientRequest request, IClientService clients, CancellationToken ct) =>
        {
            var client = await clients.UpdateAsync(key, request.Name, request.AllowedRedirectOrigins, request.IsActive, request.AllowAnonymousContentRead, ct);
            return Results.Ok(client);
        }).RequireAuthorization("ClientAdmin").RequireOwnTenant();

        return app;
    }
}

public sealed record CreateClientRequest(string Key, string Name, string[]? AllowedRedirectOrigins);

public sealed record UpdateClientRequest(string Name, string[]? AllowedRedirectOrigins, bool? IsActive, bool? AllowAnonymousContentRead);
