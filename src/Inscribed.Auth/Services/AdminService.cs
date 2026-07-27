using Inscribed.Auth.Authorization;
using Inscribed.Auth.Entities;
using Inscribed.Auth.Options;
using Inscribed.Auth.Storage.Repositories;
using Inscribed.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth.Services;

public interface IAdminService
{
    Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Client>> ListClientsAsync(CancellationToken cancellationToken = default);
    Task<ClientDetail> GetClientAsync(string key, CancellationToken cancellationToken = default);
    Task<AdminOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<Client> CreateClientAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, CancellationToken cancellationToken = default);
    Task<Client> UpdateClientAsync(string key, string name, IReadOnlyList<string>? allowedRedirectOrigins, bool? isActive, bool? allowAnonymousContentRead, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MembershipEntry>> ListMembershipsAsync(string clientKey, CancellationToken cancellationToken = default);
    Task<MembershipResult> UpsertMembershipAsync(string clientKey, string email, IReadOnlyList<string>? capabilities, CancellationToken cancellationToken = default);
    Task RemoveMembershipAsync(string clientKey, string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceKey>> ListServiceKeysAsync(string clientKey, CancellationToken cancellationToken = default);
    Task<ServiceKeyCreated> CreateServiceKeyAsync(string clientKey, string name, IReadOnlyList<string>? capabilities, DateTime? expiresAt, CancellationToken cancellationToken = default);
    Task RevokeServiceKeyAsync(string clientKey, Guid id, CancellationToken cancellationToken = default);
    string RotateSigningKey();
}

public sealed record MembershipResult(Guid UserId, string Email, string ClientKey, string[] Capabilities);

public sealed record MembershipEntry(Guid UserId, string Email, string DisplayName, bool IsActive, string[] Capabilities);

public sealed record ClientDetail(Client Client, int ServiceKeys, int ActiveServiceKeys, int Members);

public sealed record AdminOverview(int Clients, int Users, int ActiveServiceKeys, string SigningKeyId);

public sealed record ServiceKeyCreated(Guid Id, string KeyPrefix, string RawKey);

internal sealed class AdminService : IAdminService
{
    private readonly IUserRepository _users;
    private readonly IClientRepository _clients;
    private readonly IMembershipRepository _memberships;
    private readonly IServiceKeyRepository _serviceKeys;
    private readonly IServiceKeyService _serviceKeyService;
    private readonly ISigningKeyStore _signingKeys;
    private readonly AuthOptions _options;

    public AdminService(
        IUserRepository users,
        IClientRepository clients,
        IMembershipRepository memberships,
        IServiceKeyRepository serviceKeys,
        IServiceKeyService serviceKeyService,
        ISigningKeyStore signingKeys,
        IOptions<AuthOptions> options)
    {
        _options = options.Value;
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

    public async Task<ClientDetail> GetClientAsync(string key, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByKeyAsync(key, cancellationToken);
        if (client is null)
        {
            throw new NotFoundException($"Client '{key}' not found.");
        }

        var now = DateTime.UtcNow;
        var keys = await _serviceKeys.GetByClientKeyAsync(client.Key, cancellationToken);
        var members = await _memberships.CountByClientAsync(client.Id, cancellationToken);

        return new ClientDetail(client, keys.Count, keys.Count(serviceKey => serviceKey.IsActive(now)), members);
    }

    public async Task<AdminOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var clients = await _clients.GetAllAsync(cancellationToken);
        var users = await _users.GetAllAsync(cancellationToken);

        var activeKeys = 0;
        foreach (var client in clients)
        {
            var keys = await _serviceKeys.GetByClientKeyAsync(client.Key, cancellationToken);
            activeKeys += keys.Count(serviceKey => serviceKey.IsActive(now));
        }

        return new AdminOverview(clients.Count, users.Count, activeKeys, _signingKeys.GetActiveSigningCredentials().Key.KeyId);
    }

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

    public async Task<IReadOnlyList<MembershipEntry>> ListMembershipsAsync(string clientKey, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByKeyAsync(clientKey, cancellationToken);
        if (client is null)
        {
            throw new NotFoundException($"Client '{clientKey}' not found.");
        }

        var memberships = await _memberships.GetByClientAsync(client.Id, cancellationToken);
        if (memberships.Count == 0)
        {
            return [];
        }

        var users = (await _users.GetByIdsAsync([.. memberships.Select(membership => membership.UserId)], cancellationToken))
            .ToDictionary(user => user.Id);

        return
        [
            .. memberships
                .Where(membership => users.ContainsKey(membership.UserId))
                .Select(membership => new MembershipEntry(
                    membership.UserId,
                    users[membership.UserId].Email,
                    users[membership.UserId].DisplayName,
                    users[membership.UserId].IsActive,
                    membership.Roles))
                .OrderBy(entry => entry.Email, StringComparer.Ordinal)
        ];
    }

    public async Task<MembershipResult> UpsertMembershipAsync(string clientKey, string email, IReadOnlyList<string>? capabilities, CancellationToken cancellationToken = default)
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

        var resolved = CapabilityCatalog.Resolve(capabilities, GrantTarget.Membership);
        if (resolved.Intersect(CapabilityCatalog.HumanOnly, StringComparer.Ordinal).Any()
            && !string.Equals(client.Key, _options.AdminClientKey, StringComparison.Ordinal))
        {
            throw new ValidationException([$"{string.Join(", ", CapabilityCatalog.HumanOnly)} may only be granted on the '{_options.AdminClientKey}' client: administration spans every tenant."]);
        }

        var now = DateTime.UtcNow;
        var membership = await _memberships.GetAsync(user.Id, client.Id, cancellationToken);
        if (membership is null)
        {
            membership = Membership.Create(user.Id, client.Id, resolved, now);
            _memberships.Add(membership);
        }
        else
        {
            membership.SetRoles(resolved, now);
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

    public async Task<ServiceKeyCreated> CreateServiceKeyAsync(string clientKey, string name, IReadOnlyList<string>? capabilities, DateTime? expiresAt, CancellationToken cancellationToken = default)
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

        var resolved = CapabilityCatalog.Resolve(capabilities, GrantTarget.ServiceKey);

        var created = await _serviceKeyService.CreateAsync(client.Key, name, resolved, expiresAt, cancellationToken);
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
