using Skylab.Cms.Api.Authentication;
using Skylab.Cms.Application.Contracts.Requests;
using Skylab.Cms.Application.Services;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Api.Endpoints;

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
        }).RequireAuthorization("CmsAccess");

        var group = app.MapGroup("/cms/collections/{key}").RequireAuthorization("CmsAccess");

        group.MapGet("/schema", (CollectionKey key, HttpContext context, ICollectionService service) =>
        {
            var isEditor = context.User.IsInRole("cms:access");
            if (!isEditor && !service.AllowsAnonymousRead(key))
                return Results.Unauthorized();

            ApplyReadCacheHeaders(context, isEditor);
            var schema = service.GetSchema(key);
            return Results.Ok(schema);
        }).AllowAnonymous();

        group.MapGet("/", async (CollectionKey key, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var isEditor = context.User.IsInRole("cms:access");
            if (!isEditor && !service.AllowsAnonymousRead(key))
                return Results.Unauthorized();

            var userId = isEditor ? context.User.GetUserSub() ?? string.Empty : string.Empty;
            ApplyReadCacheHeaders(context, isEditor);

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

        group.MapPost("/", async (CollectionKey key, CreateCollectionItemRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.CreateAutoSlugAsync(key, request, context.User, updatedBy, ct);
            return Results.Created($"/cms/collections/{key}/{response.Slug}", response);
        });

        group.MapPost("/drafts", async (CollectionKey key, SaveNewDraftRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await service.SaveNewDraftAsync(key, userId, context.User, request, ct);
            return Results.NoContent();
        });

        group.MapGet("/{slug}", async (CollectionKey key, string slug, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var isEditor = context.User.IsInRole("cms:access");
            if (!isEditor && !service.AllowsAnonymousRead(key))
                return Results.Unauthorized();

            var userId = isEditor ? context.User.GetUserSub() ?? string.Empty : string.Empty;
            ApplyReadCacheHeaders(context, isEditor);

            var item = await service.GetAsync(key, slug, context.User, userId, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }).AllowAnonymous();

        group.MapPut("/{slug}", async (CollectionKey key, string slug, UpsertCollectionItemRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.UpsertAsync(key, slug, request, context.User, updatedBy, ct);
            return Results.Ok(response);
        });

        group.MapPut("/{slug}/draft", async (CollectionKey key, string slug, SaveDraftRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await service.SaveItemDraftAsync(key, slug, userId, context.User, request, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static void ApplyReadCacheHeaders(HttpContext context, bool isEditor)
    {
        context.Response.Headers.Vary = "Authorization";
        context.Response.Headers.CacheControl = isEditor ? "private, no-store" : $"public, max-age={PublicReadMaxAgeSeconds}, stale-while-revalidate={PublicReadStaleSeconds}";
    }
}