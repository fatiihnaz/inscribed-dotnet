using Inscribed.Application.Contracts.Policies;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Application.Services.Policies;

public interface ICollectionPolicyResolver
{
    ICollectionPolicy Resolve(string key);
    IReadOnlyCollection<ICollectionPolicy> All { get; }
}

public sealed class CollectionPolicyResolver : ICollectionPolicyResolver
{
    private readonly Dictionary<string, ICollectionPolicy> _policies;

    public CollectionPolicyResolver(IEnumerable<ICollectionPolicy> policies)
    {
        _policies = policies.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ICollectionPolicy> All => _policies.Values;

    public ICollectionPolicy Resolve(string key)
    {
        if (!_policies.TryGetValue(key, out var policy))
            throw new NotFoundException($"Unknown collection '{key}'.");

        return policy;
    }
}
