using System.Text.Json.Nodes;
using Inscribed.Domain.Entities;

namespace Inscribed.Application.Contracts.Repositories;

public interface ICollectionItemRepository
{
    Task<IReadOnlyList<CollectionItem>> ListAsync(string key, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CollectionItem> Items, int Total)> ListPagedAsync(
        string key,
        JsonObject? filterContainment,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CollectionItem?> GetBySlugAsync(string key, string slug, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task AddAsync(CollectionItem item, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
