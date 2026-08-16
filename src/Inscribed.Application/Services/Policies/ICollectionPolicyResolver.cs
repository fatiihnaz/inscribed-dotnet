using Inscribed.Application.Contracts.Policies;

namespace Inscribed.Application.Services.Policies;

public interface ICollectionPolicyResolver
{
    ICollectionPolicy Resolve(string key);

    IReadOnlyCollection<ICollectionPolicy> All { get; }

    IReadOnlyDictionary<string, IReadOnlyList<string>> Misconfigured { get; }
}
