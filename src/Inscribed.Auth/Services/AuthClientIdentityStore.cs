using Inscribed.Application.Contracts.Identity;
using Inscribed.Auth.Entities;
using Inscribed.Auth.Storage.Repositories;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Auth.Services;

internal sealed class AuthClientIdentityStore : IClientIdentityStore
{
    private readonly IClientIdentityRepository _clients;

    public AuthClientIdentityStore(IClientIdentityRepository clients)
    {
        _clients = clients;
    }

    public async Task CreateAsync(ClientIdentityRequest request, CancellationToken cancellationToken = default)
    {
        if (await _clients.GetByKeyAsync(request.Key, cancellationToken) is not null)
            throw new ConflictException($"Client identity '{request.Key}' already exists.");

        var now = DateTime.UtcNow;
        var client = ClientIdentity.Create(request.Key, request.Name, request.AllowedRedirectOrigins, now);

        if (!request.IsActive)
            client.SetActive(false, now);

        _clients.Add(client);
        await _clients.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ClientIdentityRequest request, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByKeyAsync(request.Key, cancellationToken);

        if (client is null)
            throw new NotFoundException($"Client identity '{request.Key}' not found.");

        var now = DateTime.UtcNow;
        client.Update(request.Name, request.AllowedRedirectOrigins, now);
        client.SetActive(request.IsActive, now);

        await _clients.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => await _clients.GetByKeyAsync(key, cancellationToken) is not null;
}
