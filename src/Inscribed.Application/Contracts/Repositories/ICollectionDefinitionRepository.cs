using Inscribed.Domain.Entities;

namespace Inscribed.Application.Contracts.Repositories;

public sealed record CollectionDefinitionStamp(string Key, int Version);

public interface ICollectionDefinitionRepository
{
    Task<IReadOnlyList<CollectionDefinition>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionDefinitionStamp>> ListStampsAsync(CancellationToken cancellationToken = default);

    Task<int?> GetVersionAsync(string key, CancellationToken cancellationToken = default);

    Task<CollectionDefinition?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task AddAsync(CollectionDefinition definition, CancellationToken cancellationToken = default);

    void Remove(CollectionDefinition definition);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
