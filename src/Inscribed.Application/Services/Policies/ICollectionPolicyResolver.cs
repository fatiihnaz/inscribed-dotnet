using Inscribed.Application.Contracts.Policies;

namespace Inscribed.Application.Services.Policies;

public interface ICollectionPolicyResolver
{
    Task<ICollectionPolicy> ResolveAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ICollectionPolicy>> AllAsync(CancellationToken cancellationToken = default);
}
