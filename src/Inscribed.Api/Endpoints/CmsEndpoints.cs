using Microsoft.AspNetCore.Mvc;
using Inscribed.Api.Authentication;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Services;
using Inscribed.Application.Services.Helpers;
using Inscribed.Auth.Storage.Repositories;
using Microsoft.AspNetCore.Authorization;

namespace Inscribed.Api.Endpoints;

public static class CmsEndpoints
{
    private const string SyncedByDeployPipeline = "deploy-pipeline";
    private const int PublicReadMaxAgeSeconds = 60;
    private const int PublicReadStaleSeconds = 300;

    public static IEndpointRouteBuilder MapCmsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cms/public/{clientKey}/content", async (string clientKey, string? slug, string? locale, HttpContext context, IClientRepository clients, IContentService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest("Slug is required.");

            var client = await clients.GetByKeyAsync(clientKey, ct);
            if (client is null || !client.IsActive || !client.AllowAnonymousContentRead)
                return Results.NotFound();

            context.Response.Headers.CacheControl = $"public, max-age={PublicReadMaxAgeSeconds}, stale-while-revalidate={PublicReadStaleSeconds}";

            var resolved = LocaleResolver.Resolve(client.Locales, locale, forWrite: false);
            var response = await service.GetDataBySlugAsync(clientKey, resolved, slug, ct);
            return Results.Ok(response);
        });

        var group = app.MapGroup("/cms");

        group.MapGet("/content", async (string? slug, string? locale, HttpContext context, IClientRepository clients, IContentService service, IAuthorizationService authorization, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest("Slug is required.");

            context.Response.Headers.Vary = "Authorization";

            var client = await clients.GetByKeyAsync(clientId, ct);
            var resolved = LocaleResolver.Resolve(client?.Locales ?? [], locale, forWrite: false);

            if ((await authorization.AuthorizeAsync(context.User, "ContentWrite")).Succeeded)
            {
                context.Response.Headers.CacheControl = "private, no-store";
                return Results.Ok(await service.GetBySlugAsync(clientId, resolved, userId, slug, ct));
            }

            context.Response.Headers.CacheControl = client?.AllowAnonymousContentRead == true
                ? $"public, max-age={PublicReadMaxAgeSeconds}, stale-while-revalidate={PublicReadStaleSeconds}"
                : $"private, max-age={PublicReadMaxAgeSeconds}";

            return Results.Ok(await service.GetDataBySlugAsync(clientId, resolved, slug, ct));
        }).RequireAuthorization("ContentRead");

        group.MapPut("/content", async (string? locale, HttpContext context, UpdatePageRequest request, IClientRepository clients, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var resolved = await ResolveWriteLocaleAsync(clients, clientId, locale, ct);

            var response = await service.UpdatePageAsync(clientId, resolved, request, updatedBy, ct);
            return Results.Ok(response);
        }).RequireAuthorization("ContentWrite");

        group.MapPut("/draft", async (string? locale, HttpContext context, UpdatePageRequest request, IClientRepository clients, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Slug))
                return Results.BadRequest("Slug is required.");

            var resolved = await ResolveWriteLocaleAsync(clients, clientId, locale, ct);

            await service.SaveDraftAsync(clientId, resolved, userId, request, ct);
            return Results.NoContent();
        }).RequireAuthorization("ContentWrite");

        group.MapDelete("/draft", async (string? slug, string? locale, HttpContext context, IClientRepository clients, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest("Slug is required.");

            var resolved = await ResolveWriteLocaleAsync(clients, clientId, locale, ct);

            await service.DiscardDraftAsync(clientId, resolved, userId, slug, ct);
            return Results.NoContent();
        }).RequireAuthorization("ContentWrite");

        group.MapPost("/sync", async (HttpContext context, string? locales, [FromBody] IReadOnlyList<SyncManifestRequest> manifests, IClientRepository clients, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            if (manifests is null)
                return Results.BadRequest("Request body must be a manifest array.");

            var client = await clients.GetByKeyAsync(clientId, ct);

            if (locales is not null && client is not null)
            {
                var declared = locales.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (client.SetLocales(declared, DateTime.UtcNow))
                    await clients.SaveChangesAsync(ct);
            }

            var response = await service.SyncAsync(clientId, client?.Locales ?? [], manifests, SyncedByDeployPipeline, ct);
            return Results.Ok(response);
        }).RequireAuthorization("SchemaSync");

        return app;
    }

    private static async Task<string?> ResolveWriteLocaleAsync(IClientRepository clients, string clientId, string? locale, CancellationToken cancellationToken)
    {
        var client = await clients.GetByKeyAsync(clientId, cancellationToken);
        return LocaleResolver.Resolve(client?.Locales ?? [], locale, forWrite: true);
    }
}
