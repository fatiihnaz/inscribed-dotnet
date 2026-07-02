using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inscribed.Auth.Storage.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("auth_refresh_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()").HasColumnOrder(0);

        builder.Property(x => x.UserId).IsRequired().HasColumnOrder(1);

        builder.Property(x => x.FamilyId).IsRequired();

        builder.Property(x => x.ClientKey).IsRequired().HasMaxLength(256);

        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);

        builder.Property(x => x.ExpiresAt).IsRequired();

        builder.Property(x => x.RevokedAt);

        builder.Property(x => x.ReplacedByHash).HasMaxLength(128);

        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(x => x.TokenHash).IsUnique();

        builder.HasIndex(x => x.UserId);

        builder.HasIndex(x => x.FamilyId);
    }
}
