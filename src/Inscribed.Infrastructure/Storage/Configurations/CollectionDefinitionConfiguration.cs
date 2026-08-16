using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Configurations;

internal sealed class CollectionDefinitionConfiguration : IEntityTypeConfiguration<CollectionDefinition>
{
    public void Configure(EntityTypeBuilder<CollectionDefinition> builder)
    {
        builder.ToTable("collection_definitions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()").HasColumnOrder(0);

        builder.Property(x => x.Key).IsRequired().HasMaxLength(32).HasColumnOrder(1);

        builder.Property(x => x.Document).IsRequired().HasColumnType("jsonb");

        builder.Property(x => x.UpdatedBy).IsRequired().HasMaxLength(128);

        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(x => x.Key).IsUnique();
    }
}
