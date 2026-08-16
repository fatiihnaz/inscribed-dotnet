using Inscribed.Application.Services.Policies;

namespace Inscribed.Api.Endpoints;

public static class CollectionDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/collections/reload", async (CollectionPolicyRegistry registry, CancellationToken ct) =>
        {
            var report = await registry.LoadAsync(ct);

            if (report.Complete)
                return Results.Ok(report);

            return Results.Problem(
                title: "Reloaded with errors",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Definitions that failed to parse are not being served; every other collection was reloaded.",
                extensions: new Dictionary<string, object?>
                {
                    ["loaded"] = report.Loaded,
                    ["failed"] = report.Failed,
                });
        }).RequireAuthorization("TenantAdmin");

        return app;
    }
}
