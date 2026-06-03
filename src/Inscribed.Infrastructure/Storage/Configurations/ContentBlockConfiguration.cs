using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Configurations;

internal sealed class ContentBlockConfiguration : IEntityTypeConfiguration<ContentBlock>
{
    public void Configure(EntityTypeBuilder<ContentBlock> builder)
    {
        builder.ToTable("content_blocks");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()").HasColumnOrder(0);

        builder.Property(x => x.ClientId).IsRequired().HasMaxLength(256).HasColumnOrder(1);

        builder.Property(x => x.Slug).IsRequired().HasMaxLength(512);

        builder.Property(x => x.BlockPath).IsRequired().HasMaxLength(256);

        builder.Property(x => x.BlockType).IsRequired().HasConversion<string>().HasMaxLength(32);

        builder.Property(x => x.Value).IsRequired().HasColumnType("jsonb");

        builder.Property(x => x.SortOrder).IsRequired().HasDefaultValue(0);

        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

        builder.Property(x => x.UpdatedBy).IsRequired().HasMaxLength(128);

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.IsArchived).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.ArchivedAt);

        builder.HasIndex(x => new { x.ClientId, x.Slug, x.BlockPath }).IsUnique();

        builder.HasIndex(x => new { x.ClientId, x.Slug });

        builder.HasQueryFilter(x => !x.IsArchived);
    }
}