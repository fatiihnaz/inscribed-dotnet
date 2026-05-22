using Skylab.Cms.Domain.Entities;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Contracts.Repositories;

public interface ICollectionItemRepository
{
    Task<IReadOnlyList<CollectionItem>> ListAsync(CollectionKey key, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<CollectionItem?> GetBySlugAsync(CollectionKey key, string slug, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task AddAsync(CollectionItem item, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
