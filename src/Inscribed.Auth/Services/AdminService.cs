using Inscribed.Auth.Entities;
using Inscribed.Auth.Storage.Repositories;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Auth.Services;

public interface IAdminService
{
    Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Client>> ListClientsAsync(CancellationToken cancellationToken = default);
    Task<Client> CreateClientAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, CancellationToken cancellationToken = default);
    Task<Client> UpdateClientAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, bool? isActive, bool? allowAnonymousContentRead, CancellationToken cancellationToken = default);
    Task<MembershipResult> UpsertMembershipAsync(string clientKey, string email, IReadOnlyList<string>? roles, CancellationToken cancellationToken = default);
    Task RemoveMembershipAsync(string clientKey, string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceKey>> ListServiceKeysAsync(string clientKey, CancellationToken cancellationToken = default);
    Task<ServiceKeyCreated> CreateServiceKeyAsync(string clientKey, string name, IReadOnlyList<string>? roles, DateTime? expiresAt, CancellationToken cancellationToken = default);
    Task RevokeServiceKeyAsync(string clientKey, Guid id, CancellationToken cancellationToken = default);
    string RotateSigningKey();
}

public sealed record MembershipResult(Guid UserId, string Email, string ClientKey, string[] Roles);

public sealed record ServiceKeyCreated(Guid Id, string KeyPrefix, string RawKey);

internal sealed class AdminService : IAdminService
{
    private readonly IUserRepository _users;
    private readonly IClientRepository _clients;
    private readonly IMembershipRepository _memberships;
    private readonly IServiceKeyRepository _serviceKeys;
    private readonly IServiceKeyService _serviceKeyService;
    private readonly ISigningKeyStore _signingKeys;

    public AdminService(
        IUserRepository users,
        IClientRepository clients,
        IMembershipRepository memberships,
        IServiceKeyRepository serviceKeys,
        IServiceKeyService serviceKeyService,
        ISigningKeyStore signingKeys)
    {
        _users = users;
        _clients = clients;
        _memberships = memberships;
        _serviceKeys = serviceKeys;
        _serviceKeyService = serviceKeyService;
        _signingKeys = signingKeys;
    }

    public Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken = default) => _users.GetAllAsync(cancellationToken);

    public Task<IReadOnlyList<Client>> ListClientsAsync(CancellationToken cancellationToken = default) => _clients.GetAllAsync(cancellationToken);

    public Task<IReadOnlyList<ServiceKey>> ListServiceKeysAsync(string clientKey, CancellationToken cancellationToken = default) => _serviceKeys.GetByClientKeyAsync(clientKey, cancellationToken);

    public string RotateSigningKey() => _signingKeys.Rotate();

    public async Task<Client> CreateClientAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(["key and name are required."]);
        }

        if (await _clients.GetByKeyAsync(key.Trim(), cancellationToken) is not null)
        {
            throw new ConflictException($"Client '{key}' already exists.");
        }

        var client = Client.Create(key, name, allowedRedirectOrigins, DateTime.UtcNow);
        _clients.Add(client);
        await _clients.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task<Client> UpdateClientAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, bool? isActive, bool? allowAnonymousContentRead, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByKeyAsync(key, cancellationToken);
        if (client is null)
        {
            throw new NotFoundException($"Client '{key}' not found.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(["name is required."]);
        }

        var now = DateTime.UtcNow;
        client.Update(name, allowedRedirectOrigins ?? [], allowAnonymousContentRead ?? client.AllowAnonymousContentRead, now);
        if (isActive is { } active)
        {
            client.SetActive(active, now);
        }

        await _clients.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task<MembershipResult> UpsertMembershipAsync(string clientKey, string email, IReadOnlyList<string>? roles, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByKeyAsync(clientKey, cancellationToken);
        if (client is null)
        {
            throw new NotFoundException($"Client '{clientKey}' not found.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ValidationException(["email is required."]);
        }

        var user = await _users.GetByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null)
        {
            throw new NotFoundException($"No user with e-mail '{email}'. Users are created on first login.");
        }

        var now = DateTime.UtcNow;
        var membership = await _memberships.GetAsync(user.Id, client.Id, cancellationToken);
        if (membership is null)
        {
            membership = Membership.Create(user.Id, client.Id, roles ?? [], now);
            _memberships.Add(membership);
        }
        else
        {
            membership.SetRoles(roles ?? [], now);
        }

        await _memberships.SaveChangesAsync(cancellationToken);
        return new MembershipResult(user.Id, user.Email, client.Key, membership.Roles);
    }

    public async Task RemoveMembershipAsync(string clientKey, string email, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByKeyAsync(clientKey, cancellationToken);
        var user = client is null ? null : await _users.GetByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        var membership = user is null ? null : await _memberships.GetAsync(user.Id, client!.Id, cancellationToken);
        if (membership is null)
        {
            throw new NotFoundException("Membership not found.");
        }

        _memberships.Remove(membership);
        await _memberships.SaveChangesAsync(cancellationToken);
    }

    public async Task<ServiceKeyCreated> CreateServiceKeyAsync(string clientKey, string name, IReadOnlyList<string>? roles, DateTime? expiresAt, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByKeyAsync(clientKey, cancellationToken);
        if (client is null)
        {
            throw new NotFoundException($"Client '{clientKey}' not found.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(["name is required."]);
        }

        var created = await _serviceKeyService.CreateAsync(client.Key, name, roles ?? [], expiresAt, cancellationToken);
        return new ServiceKeyCreated(created.Id, created.KeyPrefix, created.RawKey);
    }

    public async Task RevokeServiceKeyAsync(string clientKey, Guid id, CancellationToken cancellationToken = default)
    {
        var serviceKey = await _serviceKeys.GetByIdAsync(id, cancellationToken);
        if (serviceKey is null || !string.Equals(serviceKey.ClientKey, clientKey, StringComparison.Ordinal))
        {
            throw new NotFoundException("Service key not found.");
        }

        serviceKey.Revoke(DateTime.UtcNow);
        await _serviceKeys.SaveChangesAsync(cancellationToken);
    }
}
