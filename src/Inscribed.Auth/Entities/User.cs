using Inscribed.Domain.Entities;

namespace Inscribed.Auth.Entities;

public sealed class User : Entity
{
    public string Email { get; private set; } = default!;
    public string? GoogleSubject { get; private set; }
    public string DisplayName { get; private set; } = default!;
    public bool IsActive { get; private set; }

    private User() { }

    public static User Create(string email, string displayName, DateTime utcNow, string? googleSubject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = (displayName ?? string.Empty).Trim(),
            GoogleSubject = string.IsNullOrWhiteSpace(googleSubject) ? null : googleSubject,
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public void LinkGoogle(string googleSubject, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSubject);

        GoogleSubject = googleSubject;
        UpdatedAt = utcNow;
        Version += 1;
    }

    public void UpdateProfile(string displayName, DateTime utcNow)
    {
        DisplayName = (displayName ?? string.Empty).Trim();
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