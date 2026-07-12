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
        _policies = new Dictionary<string, ICollectionPolicy>(StringComparer.OrdinalIgnoreCase);

        foreach (var policy in policies)
        {
            if (_policies.TryGetValue(policy.Key, out var existing))
                throw new InvalidOperationException($"Collection key '{policy.Key}' is defined by both {Describe(existing)} and {Describe(policy)}.");

            _policies.Add(policy.Key, policy);
        }
    }

    private static string Describe(ICollectionPolicy policy)
        => policy is FileCollectionPolicy file ? $"'{file.SourceFile}'" : policy.GetType().Name;

    public IReadOnlyCollection<ICollectionPolicy> All => _policies.Values;

    public ICollectionPolicy Resolve(string key)
    {
        if (!_policies.TryGetValue(key, out var policy))
            throw new NotFoundException($"Unknown collection '{key}'.");

        return policy;
    }
}
