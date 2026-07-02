using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inscribed.Auth.Storage.Configurations;

internal sealed class ServiceKeyConfiguration : IEntityTypeConfiguration<ServiceKey>
{
    public void Configure(EntityTypeBuilder<ServiceKey> builder)
    {
        builder.ToTable("auth_service_keys");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()").HasColumnOrder(0);

        builder.Property(x => x.ClientKey).IsRequired().HasMaxLength(256).HasColumnOrder(1);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(256);

        builder.Property(x => x.KeyPrefix).IsRequired().HasMaxLength(32);

        builder.Property(x => x.KeyHash).IsRequired().HasMaxLength(128);

        builder.Property(x => x.Roles).IsRequired().HasColumnType("text[]");

        builder.Property(x => x.ExpiresAt);

        builder.Property(x => x.RevokedAt);

        builder.Property(x => x.LastUsedAt);

        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(x => x.KeyPrefix);

        builder.HasIndex(x => x.ClientKey);
    }
}
