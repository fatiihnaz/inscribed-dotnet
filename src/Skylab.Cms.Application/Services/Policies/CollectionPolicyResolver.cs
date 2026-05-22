using Skylab.Cms.Application.Contracts.Policies;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Services.Policies;

public interface ICollectionPolicyResolver
{
    ICollectionPolicy Resolve(CollectionKey key);
}

public sealed class CollectionPolicyResolver : ICollectionPolicyResolver
{
    private readonly Dictionary<CollectionKey, ICollectionPolicy> _policies;

    public CollectionPolicyResolver(IEnumerable<ICollectionPolicy> policies)
    {
        _policies = policies.ToDictionary(p => p.Key);
    }

    public ICollectionPolicy Resolve(CollectionKey key)
    {
        if (!_policies.TryGetValue(key, out var policy))
            throw new InvalidOperationException($"No policy registered for collection '{key}'.");

        return policy;
    }
}