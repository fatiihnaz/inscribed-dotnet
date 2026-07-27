using Inscribed.Domain.Entities;

namespace Inscribed.Auth.Entities;

public sealed class Client : Entity
{
    public string Key { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string[] AllowedRedirectOrigins { get; private set; } = [];
    public bool AllowAnonymousContentRead { get; private set; }
    public bool IsActive { get; private set; }

    private const int MaxKeyLength = 64;

    private Client() { }

    public static Client Create(string key, string name, IEnumerable<string>? allowedRedirectOrigins, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Client
        {
            Id = Guid.NewGuid(),
            Key = ValidateKey(key),
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

    private static string ValidateKey(string key)
    {
        var trimmed = key.Trim();

        if (trimmed.Length > MaxKeyLength)
        {
            throw new ArgumentException($"Client key must be at most {MaxKeyLength} characters.", nameof(key));
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not '-')
            {
                throw new ArgumentException("Client key may contain only lowercase letters, digits and hyphens.", nameof(key));
            }
        }

        if (trimmed.StartsWith('-') || trimmed.EndsWith('-'))
        {
            throw new ArgumentException("Client key must start and end with a letter or digit.", nameof(key));
        }

        return trimmed;
    }
}