using Skylab.Cms.Api.Authentication;
using Skylab.Cms.Application.Contracts.Requests;
using Skylab.Cms.Application.Services;

namespace Skylab.Cms.Api.Endpoints;

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

            if (string.IsNullOrWhiteSpace(slug))
                return Results.BadRequest("Slug is required.");

            var response = await service.GetBySlugAsync(clientId, slug, ct);
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

            if (string.IsNullOrWhiteSpace(request.Slug))
                return Results.BadRequest("Slug is required.");

            await service.SaveDraftAsync(clientId, request, ct);
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