namespace Inscribed.Application.Contracts.Identity;

public sealed record ClientIdentityRequest(
    string Key,
    string Name,
    IReadOnlyList<string> AllowedRedirectOrigins,
    bool IsActive
);

public interface IClientIdentityStore
{
    Task CreateAsync(ClientIdentityRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(ClientIdentityRequest request, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
