using Inscribed.Application.Contracts.Identity;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Responses;
using Inscribed.Domain.Entities;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Application.Services;

public sealed class ClientService : IClientService
{
    private readonly IClientRepository _repository;
    private readonly IClientIdentityStore _identity;

    public ClientService(IClientRepository repository, IClientIdentityStore identity)
    {
        _repository = repository;
        _identity = identity;
    }

    public async Task<ClientResponse> CreateAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            throw new ValidationException(["key and name are required."]);

        var normalizedKey = Client.ValidateKey(key);

        if (await _repository.GetByKeyAsync(normalizedKey, cancellationToken) is not null)
            throw new ConflictException($"Client '{normalizedKey}' already exists.");

        var client = Client.Create(normalizedKey, DateTime.UtcNow);
        await _repository.AddAsync(client, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var identity = new ClientIdentityRequest(normalizedKey, name.Trim(), allowedRedirectOrigins ?? [], IsActive: true);

        if (await _identity.ExistsAsync(normalizedKey, cancellationToken))
            await _identity.UpdateAsync(identity, cancellationToken);
        else
            await _identity.CreateAsync(identity, cancellationToken);

        return ToResponse(client);
    }

    public async Task<ClientResponse> UpdateAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, bool? isActive, bool? allowAnonymousContentRead, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException(["name is required."]);

        var client = await RequireAsync(key, cancellationToken);

        var now = DateTime.UtcNow;
        client.Update(allowAnonymousContentRead ?? client.AllowAnonymousContentRead, now);

        if (isActive is { } active)
            client.SetActive(active, now);

        await _repository.SaveChangesAsync(cancellationToken);

        await _identity.UpdateAsync(
            new ClientIdentityRequest(client.Key, name.Trim(), allowedRedirectOrigins ?? [], client.IsActive),
            cancellationToken);

        return ToResponse(client);
    }

    public async Task<ClientResponse> GetAsync(string key, CancellationToken cancellationToken = default)
        => ToResponse(await RequireAsync(key, cancellationToken));

    public async Task<IReadOnlyList<ClientResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var clients = await _repository.GetAllAsync(cancellationToken);
        return [.. clients.Select(ToResponse)];
    }

    public async Task<IReadOnlyList<string>> SetLocalesAsync(string key, IReadOnlyList<string> locales, CancellationToken cancellationToken = default)
    {
        var client = await RequireAsync(key, cancellationToken);

        if (client.SetLocales(locales, DateTime.UtcNow))
            await _repository.SaveChangesAsync(cancellationToken);

        return client.Locales;
    }

    private async Task<Client> RequireAsync(string key, CancellationToken cancellationToken)
        => await _repository.GetByKeyAsync(key, cancellationToken)
           ?? throw new NotFoundException($"Client '{key}' is not registered.");

    private static ClientResponse ToResponse(Client client) =>
        new(
            Id: client.Id,
            Key: client.Key,
            Locales: client.Locales,
            AllowAnonymousContentRead: client.AllowAnonymousContentRead,
            IsActive: client.IsActive,
            CreatedAt: client.CreatedAt
        );
}
