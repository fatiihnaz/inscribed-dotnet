using Inscribed.Api.Authentication;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Inscribed.Api.Endpoints;

public static class CollectionEndpoints
{
    private const int PublicReadMaxAgeSeconds = 60;
    private const int PublicReadStaleSeconds = 300;

    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cms/collections/me", (HttpContext context, ICollectionService service) =>
        {
            var mine = service.GetMyCollections(context.User);
            return Results.Ok(mine);
        }).RequireAuthorization("ContentWrite");

        var group = app.MapGroup("/cms/collections/{key}").RequireAuthorization("ContentWrite");

        group.MapGet("/schema", async (string key, HttpContext context, ICollectionService service, IAuthorizationService authorization) =>
        {
            var isPublic = service.AllowsAnonymousRead(key);
            var (canRead, isEditor) = await ResolveReadAccessAsync(authorization, context);
            if (!canRead && !isPublic)
                return Results.Unauthorized();

            ApplyReadCacheHeaders(context, isEditor, isPublic);
            var schema = service.GetSchema(key);
            return Results.Ok(schema);
        }).AllowAnonymous();

        group.MapGet("/", async (string key, HttpContext context, ICollectionService service, IAuthorizationService authorization, CancellationToken ct) =>
        {
            var isPublic = service.AllowsAnonymousRead(key);
            var (canRead, isEditor) = await ResolveReadAccessAsync(authorization, context);
            if (!canRead && !isPublic)
                return Results.Unauthorized();

            var userId = isEditor ? context.User.GetUserSub() ?? string.Empty : string.Empty;
            ApplyReadCacheHeaders(context, isEditor, isPublic);

            var query = context.Request.Query;
            var offset = int.TryParse(query["offset"], out var o) ? Math.Max(0, o) : 0;
            var limit = int.TryParse(query["limit"], out var l) ? Math.Clamp(l, 1, 100) : 50;

            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "offset", "limit" };
            var filters = query
                .Where(kv => !reserved.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

            var result = await service.ListAsync(key, context.User, userId, filters, offset, limit, ct);
            return Results.Ok(result);
        }).AllowAnonymous();

        group.MapPost("/", async (string key, CreateCollectionItemRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.CreateAutoSlugAsync(key, request, context.User, updatedBy, ct);
            return Results.Created($"/cms/collections/{response.CollectionKey}/{response.Slug}", response);
        });

        group.MapPost("/drafts", async (string key, SaveNewDraftRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await service.SaveNewDraftAsync(key, userId, context.User, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/drafts", async (string key, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await service.DiscardNewDraftAsync(key, userId, ct);
            return Results.NoContent();
        });

        group.MapGet("/{slug}", async (string key, string slug, HttpContext context, ICollectionService service, IAuthorizationService authorization, CancellationToken ct) =>
        {
            var isPublic = service.AllowsAnonymousRead(key);
            var (canRead, isEditor) = await ResolveReadAccessAsync(authorization, context);
            if (!canRead && !isPublic)
                return Results.Unauthorized();

            var userId = isEditor ? context.User.GetUserSub() ?? string.Empty : string.Empty;
            ApplyReadCacheHeaders(context, isEditor, isPublic);

            var item = await service.GetAsync(key, slug, context.User, userId, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }).AllowAnonymous();

        group.MapPut("/{slug}", async (string key, string slug, UpsertCollectionItemRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.UpsertAsync(key, slug, request, context.User, updatedBy, ct);
            return Results.Ok(response);
        });

        group.MapPut("/{slug}/draft", async (string key, string slug, SaveDraftRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await service.SaveItemDraftAsync(key, slug, userId, context.User, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{slug}/draft", async (string key, string slug, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await service.DiscardItemDraftAsync(key, slug, userId, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<(bool CanRead, bool IsEditor)> ResolveReadAccessAsync(IAuthorizationService authorization, HttpContext context)
    {
        var isEditor = (await authorization.AuthorizeAsync(context.User, "ContentWrite")).Succeeded;
        var canRead = isEditor || (await authorization.AuthorizeAsync(context.User, "ContentRead")).Succeeded;
        return (canRead, isEditor);
    }

    private static void ApplyReadCacheHeaders(HttpContext context, bool isEditor, bool isPublicCollection)
    {
        context.Response.Headers.Vary = "Authorization";
        context.Response.Headers.CacheControl = (isEditor, isPublicCollection) switch
        {
            (true, _) => "private, no-store",
            (false, true) => $"public, max-age={PublicReadMaxAgeSeconds}, stale-while-revalidate={PublicReadStaleSeconds}",
            (false, false) => $"private, max-age={PublicReadMaxAgeSeconds}",
        };
    }
}