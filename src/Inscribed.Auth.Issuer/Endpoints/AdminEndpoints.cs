using Inscribed.Auth.Issuer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inscribed.Auth.Issuer.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapInscribedAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").RequireAuthorization("TenantAdmin");

        group.MapGet("/users", async (IAdminService admin, CancellationToken ct) =>
        {
            var all = await admin.ListUsersAsync(ct);
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

        group.MapGet("/clients/{key}/memberships", async (string key, IAdminService admin, CancellationToken ct) =>
        {
            var members = await admin.ListMembershipsAsync(key, ct);
            return Results.Ok(members.Select(member => new
            {
                Id = member.UserId,
                member.Email,
                member.DisplayName,
                member.IsActive,
                member.Capabilities,
            }));
        });

        group.MapPost("/clients/{key}/memberships", async (string key, UpsertMembershipRequest request, IAdminService admin, CancellationToken ct) =>
        {
            var membership = await admin.UpsertMembershipAsync(key, request.Email, request.Capabilities, ct);
            return Results.Ok(new { Id = membership.UserId, membership.Email, membership.ClientKey, membership.Capabilities });
        });

        group.MapDelete("/clients/{key}/memberships/{email}", async (string key, string email, IAdminService admin, CancellationToken ct) =>
        {
            await admin.RemoveMembershipAsync(key, email, ct);
            return Results.NoContent();
        });

        group.MapGet("/clients/{key}/service-keys", async (string key, IAdminService admin, CancellationToken ct) =>
        {
            var all = await admin.ListServiceKeysAsync(key, ct);
            return Results.Ok(all.Select(serviceKey => new
            {
                serviceKey.Id,
                serviceKey.Name,
                serviceKey.KeyPrefix,
                Capabilities = serviceKey.Roles,
                serviceKey.ExpiresAt,
                serviceKey.RevokedAt,
                serviceKey.LastUsedAt,
                serviceKey.CreatedAt,
            }));
        });

        group.MapPost("/clients/{key}/service-keys", async (string key, CreateServiceKeyRequest request, IAdminService admin, CancellationToken ct) =>
        {
            var created = await admin.CreateServiceKeyAsync(key, request.Name, request.Capabilities, request.ExpiresAt, ct);
            return Results.Created($"/admin/clients/{key}/service-keys/{created.Id}", new
            {
                created.Id,
                created.KeyPrefix,
                Key = created.RawKey,
            });
        });

        group.MapDelete("/clients/{key}/service-keys/{id:guid}", async (string key, Guid id, IAdminService admin, CancellationToken ct) =>
        {
            await admin.RevokeServiceKeyAsync(key, id, ct);
            return Results.NoContent();
        });

        group.MapPost("/signing-keys/rotate", (IAdminService admin) =>
            Results.Ok(new { Kid = admin.RotateSigningKey() }));

        return app;
    }
}

public sealed record UpsertMembershipRequest(string Email, string[]? Capabilities);

public sealed record CreateServiceKeyRequest(string Name, string[]? Capabilities, DateTime? ExpiresAt);
