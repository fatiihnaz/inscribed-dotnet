using Inscribed.Application.Contracts.Identity;

namespace Inscribed.Auth.Identity;

internal sealed class NullClientIdentityStore : IClientIdentityStore
{
    public Task CreateAsync(ClientIdentityRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UpdateAsync(ClientIdentityRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
