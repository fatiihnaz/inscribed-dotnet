using Inscribed.Auth.Issuer.Endpoints;
using Inscribed.Auth.Issuer.Options;
using Inscribed.Auth.Issuer.Services;
using Inscribed.Auth.Issuer.Storage;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth.Issuer;

internal sealed class InscribedAuthIssuerModule : IAuthIssuerModule
{
    public void Migrate(IServiceProvider services)
        => services.GetRequiredService<AuthDbContext>().Database.Migrate();

    public void EnsureUpToDate(IServiceProvider services)
    {
        var database = services.GetRequiredService<AuthDbContext>().Database;
        var pending = database.GetPendingMigrations().ToList();

        if (pending.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{nameof(AuthDbContext)} has {pending.Count} pending migration(s) [{string.Join(", ", pending)}] but Database:MigrateOnStartup is false. "
            + "Apply migrations first (run a one-shot with RUN_MIGRATIONS_AND_EXIT=true) or set Database:MigrateOnStartup=true.");
    }

    public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ValidateTokenIssuance(services);
        services.GetRequiredService<ISigningKeyStore>().GetPublicJwks();
        services.SeedInscribedAuth();

        return Task.CompletedTask;
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapInscribedIssuerEndpoints();

    private static void ValidateTokenIssuance(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<AuthIssuerOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();

        if (environment.IsProduction()
            && (string.IsNullOrWhiteSpace(options.Issuer)
                || options.Issuer.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Auth:Issuer must be set to the public URL in Production.");
        }
    }
}
