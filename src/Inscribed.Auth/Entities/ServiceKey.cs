using Inscribed.Domain.Entities;

namespace Inscribed.Auth.Entities;

public sealed class ServiceKey : Entity
{
    public string ClientKey { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string KeyPrefix { get; private set; } = default!;
    public string KeyHash { get; private set; } = default!;
    public string[] Roles { get; private set; } = [];
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }

    private ServiceKey() { }

    public static ServiceKey Create(string clientKey, string name, string keyPrefix, string keyHash, IEnumerable<string> roles, DateTime utcNow, DateTime? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);

        return new ServiceKey
        {
            Id = Guid.NewGuid(),
            ClientKey = clientKey,
            Name = name.Trim(),
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            Roles = roles?.ToArray() ?? [],
            ExpiresAt = expiresAt,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public bool IsActive(DateTime utcNow) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > utcNow);

    public void Revoke(DateTime utcNow)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = utcNow;
        UpdatedAt = utcNow;
        Version += 1;
    }
}