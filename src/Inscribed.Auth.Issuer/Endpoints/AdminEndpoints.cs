using Inscribed.Auth.Authorization;
using Inscribed.Auth.Issuer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inscribed.Auth.Issuer.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapInscribedAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var installation = app.MapGroup("/admin").RequireAuthorization("ServiceAdmin");

        installation.MapGet("/users", async (IAdminService admin, CancellationToken ct) =>
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

        installation.MapPost("/signing-keys/rotate", (IAdminService admin) =>
            Results.Ok(new { Kid = admin.RotateSigningKey() }));

        var tenant = app.MapGroup("/admin/clients/{key}")
            .RequireAuthorization("ClientAdmin")
            .RequireOwnTenant();

        tenant.MapGet("/memberships", async (string key, IAdminService admin, CancellationToken ct) =>
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

        tenant.MapPost("/memberships", async (string key, UpsertMembershipRequest request, IAdminService admin, CancellationToken ct) =>
        {
            var membership = await admin.UpsertMembershipAsync(key, request.Email, request.Capabilities, ct);
            return Results.Ok(new { Id = membership.UserId, membership.Email, membership.ClientKey, membership.Capabilities });
        });

        tenant.MapDelete("/memberships/{email}", async (string key, string email, IAdminService admin, CancellationToken ct) =>
        {
            await admin.RemoveMembershipAsync(key, email, ct);
            return Results.NoContent();
        });

        tenant.MapGet("/service-keys", async (string key, IAdminService admin, CancellationToken ct) =>
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

        tenant.MapPost("/service-keys", async (string key, CreateServiceKeyRequest request, IAdminService admin, CancellationToken ct) =>
        {
            var created = await admin.CreateServiceKeyAsync(key, request.Name, request.Capabilities, request.ExpiresAt, ct);
            return Results.Created($"/admin/clients/{key}/service-keys/{created.Id}", new
            {
                created.Id,
                created.KeyPrefix,
                Key = created.RawKey,
            });
        });

        tenant.MapDelete("/service-keys/{id:guid}", async (string key, Guid id, IAdminService admin, CancellationToken ct) =>
        {
            await admin.RevokeServiceKeyAsync(key, id, ct);
            return Results.NoContent();
        });

        return app;
    }
}

public sealed record UpsertMembershipRequest(string Email, string[]? Capabilities);

public sealed record CreateServiceKeyRequest(string Name, string[]? Capabilities, DateTime? ExpiresAt);
