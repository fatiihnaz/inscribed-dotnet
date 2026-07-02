using Inscribed.Domain.Entities;

namespace Inscribed.Auth.Entities;

public sealed class Membership : Entity
{
    public Guid UserId { get; private set; }
    public Guid ClientId { get; private set; }
    public string[] Roles { get; private set; } = [];

    private Membership() { }

    public static Membership Create(Guid userId, Guid clientId, IEnumerable<string> roles, DateTime utcNow)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("ClientId is required.", nameof(clientId));
        }

        return new Membership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClientId = clientId,
            Roles = roles?.ToArray() ?? [],
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public void SetRoles(IEnumerable<string> roles, DateTime utcNow)
    {
        Roles = roles?.ToArray() ?? [];
        UpdatedAt = utcNow;
        Version += 1;
    }
}