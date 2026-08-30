using Inscribed.Application.Services.Policies;
using Inscribed.Auth.Options;
using Microsoft.Extensions.Options;

namespace Inscribed.Api.Endpoints;

public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/claim-requirements", async (
            ICollectionPolicyResolver policies,
            IOptions<AuthOptions> auth,
            CancellationToken ct) =>
        {
            var options = auth.Value;

            var core = new[] { "sub", options.TenantClaim, options.RolesClaim, "name", "email" };

            var collections = (await policies.AllAsync(ct))
                .Where(policy => policy.RequiredClaims.Count > 0)
                .OrderBy(policy => policy.Key, StringComparer.Ordinal)
                .Select(policy => new { key = policy.Key, claims = policy.RequiredClaims })
                .ToArray();

            var all = core
                .Concat(collections.SelectMany(entry => entry.claims))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            return Results.Ok(new { core, collections, all });
        }).RequireAuthorization("ServiceAdmin");

        return app;
    }
}
