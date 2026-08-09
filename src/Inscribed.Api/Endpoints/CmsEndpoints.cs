using Microsoft.AspNetCore.Mvc;
using Inscribed.Api.Authentication;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Services;
using Inscribed.Application.Services.Helpers;
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

        var group = app.MapGroup("/cms").RequireRegisteredClient();

        group.MapGet("/content", async (string? slug, string? locale, HttpContext context, IContentService service, IAuthorizationService authorization, CancellationToken ct) =>
        {
            var client = context.GetClient();

            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest("Slug is required.");

            context.Response.Headers.Vary = "Authorization";

            var resolved = LocaleResolver.Resolve(client.Locales, locale, forWrite: false);

            if ((await authorization.AuthorizeAsync(context.User, "ContentWrite")).Succeeded)
            {
                context.Response.Headers.CacheControl = "private, no-store";
                return Results.Ok(await service.GetBySlugAsync(client.Key, resolved, userId, slug, ct));
            }

            context.Response.Headers.CacheControl = client.AllowAnonymousContentRead
                ? $"public, max-age={PublicReadMaxAgeSeconds}, stale-while-revalidate={PublicReadStaleSeconds}"
                : $"private, max-age={PublicReadMaxAgeSeconds}";

            return Results.Ok(await service.GetDataBySlugAsync(client.Key, resolved, slug, ct));
        }).RequireAuthorization("ContentRead");

        group.MapPut("/content", async (string? locale, HttpContext context, UpdatePageRequest request, IContentService service, CancellationToken ct) =>
        {
            var client = context.GetClient();

            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var resolved = LocaleResolver.Resolve(client.Locales, locale, forWrite: true);

            var response = await service.UpdatePageAsync(client.Key, resolved, request, updatedBy, ct);
            return Results.Ok(response);
        }).RequireAuthorization("ContentWrite");

        group.MapPut("/draft", async (string? locale, HttpContext context, UpdatePageRequest request, IContentService service, CancellationToken ct) =>
        {
            var client = context.GetClient();

            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Slug))
                return Results.BadRequest("Slug is required.");

            var resolved = LocaleResolver.Resolve(client.Locales, locale, forWrite: true);

            await service.SaveDraftAsync(client.Key, resolved, userId, request, ct);
            return Results.NoContent();
        }).RequireAuthorization("ContentWrite");

        group.MapDelete("/draft", async (string? slug, string? locale, HttpContext context, IContentService service, CancellationToken ct) =>
        {
            var client = context.GetClient();

            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest("Slug is required.");

            var resolved = LocaleResolver.Resolve(client.Locales, locale, forWrite: true);

            await service.DiscardDraftAsync(client.Key, resolved, userId, slug, ct);
            return Results.NoContent();
        }).RequireAuthorization("ContentWrite");

        group.MapPost("/sync", async (HttpContext context, string? locales, [FromBody] IReadOnlyList<SyncManifestRequest> manifests, IClientService clientService, IContentService service, CancellationToken ct) =>
        {
            var client = context.GetClient();

            if (manifests is null)
                return Results.BadRequest("Request body must be a manifest array.");

            var effective = locales is null
                ? client.Locales
                : await clientService.SetLocalesAsync(
                    client.Key,
                    locales.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    ct);

            var response = await service.SyncAsync(client.Key, effective, manifests, SyncedByDeployPipeline, ct);
            return Results.Ok(response);
        }).RequireAuthorization("SchemaSync");

        return app;
    }
}
