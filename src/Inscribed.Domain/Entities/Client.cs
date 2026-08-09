namespace Inscribed.Domain.Entities;

public sealed class Client : Entity
{
    public string Key { get; private set; } = default!;
    public string[] Locales { get; private set; } = [];
    public bool AllowAnonymousContentRead { get; private set; }
    public bool IsActive { get; private set; }

    private const int MaxKeyLength = 64;
    private const int MaxLocaleLength = 16;

    private Client() { }

    public static Client Create(string key, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return new Client
        {
            Id = Guid.NewGuid(),
            Key = ValidateKey(key),
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public void Update(bool allowAnonymousContentRead, DateTime utcNow)
    {
        if (AllowAnonymousContentRead == allowAnonymousContentRead)
        {
            return;
        }

        AllowAnonymousContentRead = allowAnonymousContentRead;
        UpdatedAt = utcNow;
        Version += 1;
    }

    public bool SetLocales(IEnumerable<string>? locales, DateTime utcNow)
    {
        var validated = ValidateLocales(locales);

        if (Locales.SequenceEqual(validated, StringComparer.Ordinal))
        {
            return false;
        }

        Locales = validated;
        UpdatedAt = utcNow;
        Version += 1;
        return true;
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

    public static string ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

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

    private static string[] ValidateLocales(IEnumerable<string>? locales)
    {
        if (locales is null)
        {
            return [];
        }

        var validated = new List<string>();

        foreach (var locale in locales)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locale);

            var trimmed = locale.Trim();

            if (trimmed.Length > MaxLocaleLength)
            {
                throw new ArgumentException($"Locale must be at most {MaxLocaleLength} characters.", nameof(locales));
            }

            foreach (var character in trimmed)
            {
                if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not '-')
                {
                    throw new ArgumentException("Locale may contain only lowercase letters, digits and hyphens.", nameof(locales));
                }
            }

            if (trimmed.StartsWith('-') || trimmed.EndsWith('-'))
            {
                throw new ArgumentException("Locale must start and end with a letter or digit.", nameof(locales));
            }

            if (validated.Contains(trimmed, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Locale '{trimmed}' is listed more than once.", nameof(locales));
            }

            validated.Add(trimmed);
        }

        return [.. validated];
    }
}
