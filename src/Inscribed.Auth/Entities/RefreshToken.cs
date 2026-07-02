using Inscribed.Domain.Entities;

namespace Inscribed.Auth.Entities;

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; private set; }
    public string ClientKey { get; private set; } = default!;
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? ReplacedByHash { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Issue(Guid userId, string clientKey, string tokenHash, DateTime expiresAt, DateTime utcNow, Guid? familyId = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        var id = Guid.NewGuid();

        return new RefreshToken
        {
            Id = id,
            FamilyId = familyId ?? id,
            UserId = userId,
            ClientKey = clientKey,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;

    public void Revoke(DateTime utcNow, string? replacedByHash = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = utcNow;
        ReplacedByHash = replacedByHash;
        UpdatedAt = utcNow;
        Version += 1;
    }
}