using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Configurations;

internal sealed class CollectionItemConfiguration : IEntityTypeConfiguration<CollectionItem>
{
    public void Configure(EntityTypeBuilder<CollectionItem> builder)
    {
        builder.ToTable("collection_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()").HasColumnOrder(0);

        builder.Property(x => x.CollectionKey).IsRequired().HasMaxLength(32).HasColumnOrder(1);

        builder.Property(x => x.Slug).IsRequired().HasMaxLength(256);

        builder.Property(x => x.Data).IsRequired().HasColumnType("jsonb");

        builder.Property(x => x.UpdatedBy).IsRequired().HasMaxLength(128);

        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.IsArchived).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.ArchivedAt);

        builder.HasIndex(x => new { x.CollectionKey, x.Slug }).IsUnique();
        builder.HasIndex(x => new { x.CollectionKey, x.IsArchived });

        builder.HasQueryFilter(x => !x.IsArchived);
    }
}