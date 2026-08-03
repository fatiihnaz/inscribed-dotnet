using Microsoft.EntityFrameworkCore;
using Inscribed.Domain.Entities;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Infrastructure.Storage;

public sealed class CmsDbContext : DbContext
{
    public CmsDbContext(DbContextOptions<CmsDbContext> options) : base(options)
    {
    }

    public DbSet<ContentBlock> ContentBlocks => Set<ContentBlock>();

    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CmsDbContext).Assembly);

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