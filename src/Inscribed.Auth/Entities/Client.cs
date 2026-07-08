using Inscribed.Domain.Entities;

namespace Inscribed.Auth.Entities;

public sealed class Client : Entity
{
    public string Key { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string[] AllowedRedirectOrigins { get; private set; } = [];
    public bool AllowAnonymousContentRead { get; private set; }
    public bool IsActive { get; private set; }

    private Client() { }

    public static Client Create(string key, string name, IEnumerable<string>? allowedRedirectOrigins, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Client
        {
            Id = Guid.NewGuid(),
            Key = key.Trim(),
            Name = name.Trim(),
            AllowedRedirectOrigins = allowedRedirectOrigins?.ToArray() ?? [],
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public void Update(string name, IEnumerable<string> allowedRedirectOrigins, bool allowAnonymousContentRead, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        AllowedRedirectOrigins = allowedRedirectOrigins?.ToArray() ?? [];
        AllowAnonymousContentRead = allowAnonymousContentRead;
        UpdatedAt = utcNow;
        Version += 1;
    }

    public void SetActive(bool isActive, DateTime utcNow)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        UpdatedAt = utcNow;
        Version += 1;
    }
}