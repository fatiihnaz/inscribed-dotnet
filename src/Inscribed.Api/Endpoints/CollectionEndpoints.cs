using Inscribed.Api.Authentication;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Contracts.Responses;
using Inscribed.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Inscribed.Api.Endpoints;

public static class CollectionEndpoints
{
    private const int PublicReadMaxAgeSeconds = 60;
    private const int PublicReadStaleSeconds = 300;

    private static readonly HashSet<string> ReservedQueryKeys =
        new(StringComparer.OrdinalIgnoreCase) { "offset", "limit", "locale", "sort", "archived" };

    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cms/collections/me", async (HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var mine = await service.GetMyCollectionsAsync(context.User, ct);
            return Results.Ok(mine);
        }).RequireAuthorization("ContentWrite");

        var group = app.MapGroup("/cms/collections/{key}").RequireAuthorization("ContentWrite");

        group.MapGet("/schema", async (string key, HttpContext context, ICollectionService service, IAuthorizationService authorization, CancellationToken ct) =>
        {
            var isPublic = await service.AllowsAnonymousReadAsync(key, ct);
            var (canRead, isEditor) = await ResolveReadAccessAsync(authorization, context);
            if (!canRead && !isPublic)
                return Results.Unauthorized();

            ApplyReadCacheHeaders(context, isEditor, isPublic);
            var schema = await service.GetSchemaAsync(key, context.User, ct);
            return Results.Ok(schema);
        }).AllowAnonymous();

        group.MapGet("/", async (string key, HttpContext context, ICollectionService service, IAuthorizationService authorization, CancellationToken ct) =>
        {
            var isPublic = await service.AllowsAnonymousReadAsync(key, ct);
            var (canRead, isEditor) = await ResolveReadAccessAsync(authorization, context);
            if (!canRead && !isPublic)
                return Results.Unauthorized();

            var userId = isEditor ? context.User.GetUserSub() ?? string.Empty : string.Empty;
            ApplyReadCacheHeaders(context, isEditor, isPublic);

            var query = context.Request.Query;
            var offset = int.TryParse(query["offset"], out var o) ? Math.Max(0, o) : 0;
            var limit = int.TryParse(query["limit"], out var l) ? Math.Clamp(l, 1, 100) : 50;
            var locale = query["locale"].ToString();
            var sort = query["sort"].ToString();
            var archived = bool.TryParse(query["archived"], out var a) && a;

            var filters = query
                .Where(kv => !ReservedQueryKeys.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

            var result = await service.ListAsync(key, locale, context.User, userId, filters, sort, archived, offset, limit, ct);
            return Results.Ok(result);
        }).AllowAnonymous();

        group.MapPost("/", async (string key, string? locale, Guid? translationGroup, CreateCollectionItemRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.CreateAutoSlugAsync(key, locale, translationGroup, request, context.User, updatedBy, ct);
            return Results.Created($"/cms/collections/{response.CollectionKey}/{response.Slug}", response);
        });

        group.MapPost("/drafts", async (string key, string? locale, Guid? translationGroup, SavePendingDraftRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await service.SavePendingDraftAsync(key, locale, translationGroup, userId, context.User, request, ct);
            return Results.NoContent();
        });

        group.MapDelete("/drafts", async (string key, string? locale, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await service.DiscardPendingDraftAsync(key, locale, userId, ct);
            return Results.NoContent();
        });

        group.MapGet("/{slug}", async (string key, string slug, string? locale, HttpContext context, ICollectionService service, IAuthorizationService authorization, CancellationToken ct) =>
        {
            var isPublic = await service.AllowsAnonymousReadAsync(key, ct);
            var (canRead, isEditor) = await ResolveReadAccessAsync(authorization, context);
            if (!canRead && !isPublic)
                return Results.Unauthorized();

            var userId = isEditor ? context.User.GetUserSub() ?? string.Empty : string.Empty;
            ApplyReadCacheHeaders(context, isEditor, isPublic);

            var item = await service.GetAsync(key, slug, locale, context.User, userId, ct);
            if (item is not null)
            {
                if (!string.Equals(slug, item.Slug, StringComparison.Ordinal))
                    context.Response.Headers.ContentLocation = $"/cms/collections/{key}/{item.Slug}";

                return Results.Ok(item);
            }

            var virtualItem = await service.GetVirtualAsync(key, slug, context.User, userId, ct);
            return virtualItem is null ? Results.NotFound() : Results.Ok(virtualItem);
        }).AllowAnonymous();

        group.MapPut("/{slug}", async (string key, string slug, string? locale, Guid? translationGroup, bool? replaceAlias, UpsertCollectionItemRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.UpsertAsync(key, slug, locale, translationGroup, request, context.User, updatedBy, replaceAlias ?? false, ct);
            return Results.Ok(response);
        });

        group.MapPut("/{slug}/slug", async (string key, string slug, bool? replaceAlias, RenameSlugRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.RenameSlugAsync(key, slug, request, context.User, updatedBy, replaceAlias ?? false, ct);
            return Results.Ok(response);
        });

        group.MapDelete("/{slug}/alias", async (string key, string slug, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(context.User.GetUserSub()))
                return Results.Unauthorized();

            await service.ReleaseAliasAsync(key, slug, context.User, ct);
            return Results.NoContent();
        });

        group.MapDelete("/{slug}", async (string key, string slug, int? version, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.ArchiveAsync(key, slug, version, context.User, updatedBy, ct);
            return Results.Ok(response);
        });

        group.MapPost("/{slug}/restore", async (string key, string slug, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.RestoreAsync(key, slug, context.User, updatedBy, ct);
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