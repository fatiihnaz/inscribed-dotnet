using Microsoft.EntityFrameworkCore;
using Inscribed.Auth.Storage;
using Inscribed.Infrastructure.Storage;

namespace Inscribed.Api.Startup;

internal static class DatabaseMigrator
{
    public static void MigrateAll(IServiceProvider services)
    {
        services.GetRequiredService<CmsDbContext>().Database.Migrate();
        services.GetRequiredService<AuthDbContext>().Database.Migrate();
    }

    public static void EnsureUpToDate(IServiceProvider services)
    {
        EnsureContextUpToDate(services.GetRequiredService<CmsDbContext>(), nameof(CmsDbContext));
        EnsureContextUpToDate(services.GetRequiredService<AuthDbContext>(), nameof(AuthDbContext));
    }

    private static void EnsureContextUpToDate(DbContext db, string name)
    {
        var pending = db.Database.GetPendingMigrations().ToList();
        if (pending.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{name} has {pending.Count} pending migration(s) [{string.Join(", ", pending)}] but Database:MigrateOnStartup is false. " +
            "Apply migrations first (run a one-shot with RUN_MIGRATIONS_AND_EXIT=true) or set Database:MigrateOnStartup=true.");
    }
}