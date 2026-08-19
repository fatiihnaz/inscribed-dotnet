using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Configurations;

internal sealed class CollectionSlugAliasConfiguration : IEntityTypeConfiguration<CollectionSlugAlias>
{
    public void Configure(EntityTypeBuilder<CollectionSlugAlias> builder)
    {
        builder.ToTable("collection_slug_aliases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()").HasColumnOrder(0);

        builder.Property(x => x.CollectionKey).IsRequired().HasMaxLength(32).HasColumnOrder(1);

        builder.Property(x => x.Slug).IsRequired().HasMaxLength(256);

        builder.Property(x => x.ItemId).IsRequired();

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(x => new { x.CollectionKey, x.Slug }).IsUnique();
        builder.HasIndex(x => x.ItemId);

        builder.HasOne<CollectionItem>()
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
