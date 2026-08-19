using Inscribed.Domain.Entities;

namespace Inscribed.Application.Contracts.Repositories;

public interface ICollectionSlugAliasRepository
{
    Task<CollectionSlugAlias?> GetAsync(string key, string slug, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, string slug, CancellationToken cancellationToken = default);

    Task AddAsync(CollectionSlugAlias alias, CancellationToken cancellationToken = default);

    void Remove(CollectionSlugAlias alias);
}
