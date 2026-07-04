using Inscribed.Domain.Entities;

namespace Inscribed.Auth.Entities;

public sealed class SigningKey : Entity
{
    public string Kid { get; private set; } = default!;
    public string Algorithm { get; private set; } = default!;
    public string PublicKeyPem { get; private set; } = default!;
    public string PrivateKeyPem { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime? ExpiresAt { get; private set; }

    private SigningKey() { }

    public static SigningKey Create(
        string kid,
        string algorithm,
        string publicKeyPem,
        string privateKeyPem,
        DateTime utcNow,
        DateTime? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        return new SigningKey
        {
            Id = Guid.NewGuid(),
            Kid = kid,
            Algorithm = algorithm,
            PublicKeyPem = publicKeyPem,
            PrivateKeyPem = privateKeyPem,
            IsActive = true,
            ExpiresAt = expiresAt,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public void Deactivate(DateTime utcNow, DateTime? expiresAt = null)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        ExpiresAt = expiresAt ?? ExpiresAt;
        UpdatedAt = utcNow;
        Version += 1;
    }
}