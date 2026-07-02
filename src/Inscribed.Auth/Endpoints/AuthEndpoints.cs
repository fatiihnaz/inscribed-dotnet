using Inscribed.Auth.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inscribed.Auth.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapInscribedAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/jwks.json", (HttpContext context, ISigningKeyStore keys) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Json(new { keys = keys.GetPublicJwks() });
        });

        return app;
    }
}