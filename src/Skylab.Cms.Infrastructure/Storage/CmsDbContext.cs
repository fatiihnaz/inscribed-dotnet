using Microsoft.EntityFrameworkCore;
using Skylab.Cms.Domain.Entities;

namespace Skylab.Cms.Infrastructure.Storage;

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
}