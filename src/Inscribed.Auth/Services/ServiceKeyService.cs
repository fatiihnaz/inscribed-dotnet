using System.Security.Cryptography;
using System.Text;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Entities;
using Inscribed.Auth.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Services;

internal static class ServiceKeyFormat
{
    public const string Prefix = "ink_live_";
    public const int PrefixLength = 16;

    public static (string RawKey, string KeyPrefix, string KeyHash) Generate()
    {
        var rawKey = Prefix + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (rawKey, rawKey[..PrefixLength], Hash(rawKey));
    }

    public static string Hash(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(rawKey)));
}

internal interface IServiceKeyService
{
    Task<CreatedServiceKey> CreateAsync(string clientKey, string name, IReadOnlyList<string> roles, DateTime? expiresAt = null, CancellationToken cancellationToken = default);
    Task<ValidatedServiceKey?> ValidateAsync(string rawKey, CancellationToken cancellationToken = default);
}

internal sealed record CreatedServiceKey(Guid Id, string RawKey, string KeyPrefix);

internal sealed record ValidatedServiceKey(Guid Id, string ClientKey, string Name, string[] Roles);

internal sealed class ServiceKeyService : IServiceKeyService
{
    private readonly IServiceKeyRepository _serviceKeys;
    private readonly IClientIdentityRepository _clients;
    private readonly ILogger<ServiceKeyService> _logger;

    public ServiceKeyService(
        IServiceKeyRepository serviceKeys,
        IClientIdentityRepository clients,
        ILogger<ServiceKeyService> logger)
    {
        _serviceKeys = serviceKeys;
        _clients = clients;
        _logger = logger;
    }

    public async Task<CreatedServiceKey> CreateAsync(string clientKey, string name, IReadOnlyList<string> roles, DateTime? expiresAt = null, CancellationToken cancellationToken = default)
    {
        var (rawKey, keyPrefix, keyHash) = ServiceKeyFormat.Generate();
        var key = ServiceKey.Create(clientKey, name, keyPrefix, keyHash, roles, DateTime.UtcNow, expiresAt);

        _serviceKeys.Add(key);
        await _serviceKeys.SaveChangesAsync(cancellationToken);

        return new CreatedServiceKey(key.Id, rawKey, keyPrefix);
    }

    public async Task<ValidatedServiceKey?> ValidateAsync(string rawKey, CancellationToken cancellationToken = default)
    {
        if (!rawKey.StartsWith(ServiceKeyFormat.Prefix, StringComparison.Ordinal) || rawKey.Length <= ServiceKeyFormat.PrefixLength)
        {
            return null;
        }

        var candidates = await _serviceKeys.GetByPrefixAsync(rawKey[..ServiceKeyFormat.PrefixLength], cancellationToken);
        if (candidates.Count == 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(rawKey));

        foreach (var candidate in candidates)
        {
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(candidate.KeyHash), hash))
            {
                continue;
            }

            if (!candidate.IsActive(now))
            {
                return null;
            }

            var client = await _clients.GetByKeyAsync(candidate.ClientKey, cancellationToken);
            if (client is null || !client.IsActive)
            {
                return null;
            }

            await _serviceKeys.TouchLastUsedAsync(candidate.Id, now, cancellationToken);
            return new ValidatedServiceKey(candidate.Id, candidate.ClientKey, candidate.Name, WithoutHumanOnlyCapabilities(candidate));
        }

        return null;
    }

    private string[] WithoutHumanOnlyCapabilities(ServiceKey key)
    {
        var refused = key.Roles.Intersect(CapabilityCatalog.HumanOnly, StringComparer.OrdinalIgnoreCase).ToArray();

        if (refused.Length == 0)
        {
            return key.Roles;
        }

        _logger.LogWarning(
            "Service key {ServiceKeyId} on client '{ClientKey}' carries {Capabilities}; those are refused to machine principals. Revoke and re-issue it.",
            key.Id,
            key.ClientKey,
            string.Join(", ", refused));

        return [.. key.Roles.Except(refused, StringComparer.OrdinalIgnoreCase)];
    }
}
