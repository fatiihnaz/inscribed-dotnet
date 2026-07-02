using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inscribed.Auth.Storage.Configurations;

internal sealed class SigningKeyConfiguration : IEntityTypeConfiguration<SigningKey>
{
    public void Configure(EntityTypeBuilder<SigningKey> builder)
    {
        builder.ToTable("auth_signing_keys");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()").HasColumnOrder(0);

        builder.Property(x => x.Kid).IsRequired().HasMaxLength(64);

        builder.Property(x => x.Algorithm).IsRequired().HasMaxLength(16);

        builder.Property(x => x.PublicKeyPem).IsRequired().HasColumnType("text");

        builder.Property(x => x.PrivateKeyPem).IsRequired().HasColumnType("text");

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(x => x.ExpiresAt);

        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(x => x.Kid).IsUnique();
    }
}
