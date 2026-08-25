using Inscribed.Auth.Issuer.Entities;
using Inscribed.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Inscribed.Auth.Issuer.Storage;

public sealed class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<ClientIdentity> Clients => Set<ClientIdentity>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ServiceKey> ServiceKeys => Set<ServiceKey>();

    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw Conflict(ex);
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw Conflict(ex);
        }
    }

    private static ConcurrencyConflictException Conflict(DbUpdateConcurrencyException ex)
    {
        var subjects = ex.Entries.Select(entry => entry.Metadata.ClrType.Name).Distinct().ToList();
        var subject = subjects.Count > 0 ? string.Join(", ", subjects) : "record";

        return new ConcurrencyConflictException(
            $"A concurrent write changed {subject} after it was read. Re-read the current version and retry.", ex);
    }
}
