using Inscribed.Application.Contracts.Policies;
using Inscribed.Domain.Enums;

namespace Inscribed.Application.Services.Policies;

public interface ICollectionPolicyResolver
{
    ICollectionPolicy Resolve(CollectionKey key);
    IReadOnlyCollection<ICollectionPolicy> All { get; }
}

public sealed class CollectionPolicyResolver : ICollectionPolicyResolver
{
    private readonly Dictionary<CollectionKey, ICollectionPolicy> _policies;

    public CollectionPolicyResolver(IEnumerable<ICollectionPolicy> policies)
    {
        _policies = policies.ToDictionary(p => p.Key);
    }

    public IReadOnlyCollection<ICollectionPolicy> All => _policies.Values;

    public ICollectionPolicy Resolve(CollectionKey key)
    {
        if (!_policies.TryGetValue(key, out var policy))
            throw new InvalidOperationException($"No policy registered for collection '{key}'.");

        return policy;
    }
}