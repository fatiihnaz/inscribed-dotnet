using System.Reflection;
using Inscribed.Auth;
using Inscribed.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Inscribed.Api.Endpoints;

public static class HealthEndpoints
{
    private const string ReadinessProbeKey = "inscribed:health";

    private static readonly string Version = ReadVersion();

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", (IServiceProvider services) => Results.Ok(new
        {
            status = "healthy",
            version = Version,
            issuer = DescribeIssuer(services),
        })).AllowAnonymous();

        app.MapGet("/health/ready", async (
            CmsDbContext cms,
            IDistributedCache cache,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            var database = await CheckAsync(async () => await cms.Database.CanConnectAsync(ct));
            var redis = await CheckAsync(async () =>
            {
                await cache.GetAsync(ReadinessProbeKey, ct);
                return true;
            });
            var migrations = await CheckAsync(async () => !(await cms.Database.GetPendingMigrationsAsync(ct)).Any());

            var ready = database.Ok && redis.Ok && migrations.Ok;

            var payload = new
            {
                status = ready ? "ready" : "not ready",
                version = Version,
                issuer = DescribeIssuer(services),
                checks = new
                {
                    database = database.Detail,
                    redis = redis.Detail,
                    migrations = migrations.Detail,
                },
            };

            return ready
                ? Results.Ok(payload)
                : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();

        return app;
    }

    private static async Task<(bool Ok, string Detail)> CheckAsync(Func<Task<bool>> probe)
    {
        try
        {
            return await probe() ? (true, "ok") : (false, "failed");
        }
        catch (Exception exception)
        {
            return (false, exception.GetType().Name);
        }
    }

    private static string DescribeIssuer(IServiceProvider services)
        => services.GetServices<IAuthIssuerModule>().Any() ? "built-in" : "external";

    private static string ReadVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0-dev";
        }

        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
