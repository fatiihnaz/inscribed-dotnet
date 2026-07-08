using Inscribed.Auth.Entities;
using Inscribed.Auth.Services;
using Inscribed.Auth.Storage.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inscribed.Auth.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapInscribedAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").RequireAuthorization("AdminAccess");

        group.MapGet("/users", async (IUserRepository users, CancellationToken ct) =>
        {
            var all = await users.GetAllAsync(ct);
            return Results.Ok(all.Select(user => new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                GoogleLinked = user.GoogleSubject is not null,
                user.IsActive,
                user.CreatedAt,
            }));
        });

        group.MapGet("/clients", async (IClientRepository clients, CancellationToken ct) =>
        {
            var all = await clients.GetAllAsync(ct);
            return Results.Ok(all.Select(ToClientResponse));
        });

        group.MapPost("/clients", async (CreateClientRequest request, IClientRepository clients, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("key and name are required.");
            }

            if (await clients.GetByKeyAsync(request.Key.Trim(), ct) is not null)
            {
                return Results.Conflict($"Client '{request.Key}' already exists.");
            }

            var client = Client.Create(request.Key, request.Name, request.AllowedRedirectOrigins ?? [], DateTime.UtcNow);
            clients.Add(client);
            await clients.SaveChangesAsync(ct);

            return Results.Created($"/admin/clients/{client.Key}", ToClientResponse(client));
        });

        group.MapPut("/clients/{key}", async (string key, UpdateClientRequest request, IClientRepository clients, CancellationToken ct) =>
        {
            var client = await clients.GetByKeyAsync(key, ct);
            if (client is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("name is required.");
            }

            var now = DateTime.UtcNow;
            client.Update(
                request.Name,
                request.AllowedRedirectOrigins ?? [],
                request.AllowAnonymousContentRead ?? client.AllowAnonymousContentRead,
                now);
            if (request.IsActive is { } isActive)
            {
                client.SetActive(isActive, now);
            }

            await clients.SaveChangesAsync(ct);
            return Results.Ok(ToClientResponse(client));
        });

        group.MapPost("/clients/{key}/memberships", async (string key, UpsertMembershipRequest request, IClientRepository clients, IUserRepository users, IMembershipRepository memberships, CancellationToken ct) =>
        {
            var client = await clients.GetByKeyAsync(key, ct);
            if (client is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Results.BadRequest("email is required.");
            }

            var user = await users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), ct);
            if (user is null)
            {
                return Results.NotFound($"No user with e-mail '{request.Email}'. Users are created on first login.");
            }

            var now = DateTime.UtcNow;
            var membership = await memberships.GetAsync(user.Id, client.Id, ct);
            if (membership is null)
            {
                membership = Membership.Create(user.Id, client.Id, request.Roles ?? [], now);
                memberships.Add(membership);
            }
            else
            {
                membership.SetRoles(request.Roles ?? [], now);
            }

            await memberships.SaveChangesAsync(ct);
            return Results.Ok(new { user.Id, user.Email, ClientKey = client.Key, membership.Roles });
        });

        group.MapDelete("/clients/{key}/memberships/{email}", async (string key, string email, IClientRepository clients, IUserRepository users, IMembershipRepository memberships, CancellationToken ct) =>
        {
            var client = await clients.GetByKeyAsync(key, ct);
            var user = client is null ? null : await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), ct);
            var membership = user is null ? null : await memberships.GetAsync(user.Id, client!.Id, ct);
            if (membership is null)
            {
                return Results.NotFound();
            }

            memberships.Remove(membership);
            await memberships.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/clients/{key}/service-keys", async (string key, IServiceKeyRepository serviceKeys, CancellationToken ct) =>
        {
            var all = await serviceKeys.GetByClientKeyAsync(key, ct);
            return Results.Ok(all.Select(serviceKey => new
            {
                serviceKey.Id,
                serviceKey.Name,
                serviceKey.KeyPrefix,
                serviceKey.Roles,
                serviceKey.ExpiresAt,
                serviceKey.RevokedAt,
                serviceKey.LastUsedAt,
                serviceKey.CreatedAt,
            }));
        });

        group.MapPost("/clients/{key}/service-keys", async (string key, CreateServiceKeyRequest request, IClientRepository clients, IServiceKeyService serviceKeys, CancellationToken ct) =>
        {
            var client = await clients.GetByKeyAsync(key, ct);
            if (client is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("name is required.");
            }

            var created = await serviceKeys.CreateAsync(client.Key, request.Name, request.Roles ?? [], request.ExpiresAt, ct);
            return Results.Created($"/admin/clients/{client.Key}/service-keys/{created.Id}", new
            {
                created.Id,
                created.KeyPrefix,
                Key = created.RawKey,
            });
        });

        group.MapDelete("/clients/{key}/service-keys/{id:guid}", async (string key, Guid id, IServiceKeyRepository serviceKeys, CancellationToken ct) =>
        {
            var serviceKey = await serviceKeys.GetByIdAsync(id, ct);
            if (serviceKey is null || !string.Equals(serviceKey.ClientKey, key, StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            serviceKey.Revoke(DateTime.UtcNow);
            await serviceKeys.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/signing-keys/rotate", (ISigningKeyStore signingKeys) =>
            Results.Ok(new { Kid = signingKeys.Rotate() }));

        return app;
    }

    private static object ToClientResponse(Client client) => new
    {
        client.Id,
        client.Key,
        client.Name,
        client.AllowedRedirectOrigins,
        client.AllowAnonymousContentRead,
        client.IsActive,
        client.CreatedAt,
    };
}

public sealed record CreateClientRequest(string Key, string Name, string[]? AllowedRedirectOrigins);

public sealed record UpdateClientRequest(string Name, string[]? AllowedRedirectOrigins, bool? IsActive, bool? AllowAnonymousContentRead);

public sealed record UpsertMembershipRequest(string Email, string[]? Roles);

public sealed record CreateServiceKeyRequest(string Name, string[]? Roles, DateTime? ExpiresAt);
