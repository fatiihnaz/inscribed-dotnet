using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Configurations;

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("clients");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()").HasColumnOrder(0);

        builder.Property(x => x.Key).IsRequired().HasMaxLength(64).HasColumnOrder(1);

        builder.Property(x => x.Locales).IsRequired().HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");

        builder.Property(x => x.AllowAnonymousContentRead).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(x => x.Key).IsUnique();
    }
}
