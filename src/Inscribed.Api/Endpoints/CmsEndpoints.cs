using Inscribed.Api.Authentication;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Services;

namespace Inscribed.Api.Endpoints;

public static class CmsEndpoints
{
    private const string SyncedByDeployPipeline = "deploy-pipeline";

    public static IEndpointRouteBuilder MapCmsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cms").RequireAuthorization("CmsAccess");

        group.MapGet("/content", async (string? slug, HttpContext context, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest("Slug is required.");

            var response = await service.GetBySlugAsync(clientId, userId, slug, ct);
            return Results.Ok(response);
        });

        group.MapGet("/data", async (string? slug, HttpContext context, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest("Slug is required.");

            var response = await service.GetDataBySlugAsync(clientId, slug, ct);
            return Results.Ok(response);
        });

        group.MapPut("/content", async (HttpContext context, UpdatePageRequest request, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            var updatedBy = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(updatedBy))
                return Results.Unauthorized();

            var response = await service.UpdatePageAsync(clientId, request, updatedBy, ct);
            return Results.Ok(response);
        });

        group.MapPut("/draft", async (HttpContext context, UpdatePageRequest request, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            var userId = context.User.GetUserSub();
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Slug))
                return Results.BadRequest("Slug is required.");

            await service.SaveDraftAsync(clientId, userId, request, ct);
            return Results.NoContent();
        });

        group.MapPost("/sync", async (HttpContext context, SyncManifestRequest request, IContentService service, CancellationToken ct) =>
        {
            var clientId = context.User.GetClientId();
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            var response = await service.SyncAsync(clientId, request, SyncedByDeployPipeline, ct);
            return Results.Ok(response);
        });

        return app;
    }
}