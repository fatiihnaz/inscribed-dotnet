using Skylab.Cms.Api.Authentication;
using Skylab.Cms.Application.Contracts.Requests;
using Skylab.Cms.Application.Services;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Api.Endpoints;

public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cms/collections/me", (HttpContext context, ICollectionService service) =>
        {
            var mine = service.GetMyCollections(context.User);
            return Results.Ok(mine);
        }).RequireAuthorization("CmsAccess");

        var group = app.MapGroup("/cms/collections/{key}").RequireAuthorization("CmsAccess");

        group.MapPost("/", async (CollectionKey key, CreateCollectionItemRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.CreateAutoSlugAsync(key, request, context.User, updatedBy, ct);
            return Results.Created($"/cms/collections/{key}/{response.Slug}", response);
        });

        group.MapGet("/schema", (CollectionKey key, ICollectionService service) =>
        {
            var schema = service.GetSchema(key);
            return Results.Ok(schema);
        });

        group.MapGet("/", async (CollectionKey key, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var items = await service.ListAsync(key, context.User, ct);
            return Results.Ok(items);
        });

        group.MapGet("/{slug}", async (CollectionKey key, string slug, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var item = await service.GetAsync(key, slug, context.User, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPut("/{slug}", async (CollectionKey key, string slug, UpsertCollectionItemRequest request, HttpContext context, ICollectionService service, CancellationToken ct) =>
        {
            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.UpsertAsync(key, slug, request, context.User, updatedBy, ct);
            return Results.Ok(response);
        });

        return app;
    }
}
