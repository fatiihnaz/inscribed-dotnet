using System.Text.Json.Nodes;
using Inscribed.Domain.Entities;

namespace Inscribed.Application.Contracts.Repositories;

public interface ICollectionItemRepository
{
    Task<IReadOnlyList<CollectionItem>> ListAsync(string key, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CollectionItem> Items, int Total)> ListPagedAsync(
        string key,
        string? locale,
        JsonObject? filterContainment,
        CollectionSort sort,
        bool archived,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CollectionItem?> GetBySlugAsync(string key, string slug, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TakenSlug>> GetTakenSlugsAsync(string key, IReadOnlyCollection<string> slugs, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionItem>> GetBySlugsAsync(string key, IReadOnlyCollection<string> slugs, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionItem>> GetByTranslationGroupAsync(string key, Guid translationGroupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionItem>> GetByTranslationGroupsAsync(string key, IReadOnlyCollection<Guid> translationGroupIds, CancellationToken cancellationToken = default);

    Task<int> CountAsync(string key, CancellationToken cancellationToken = default);

    Task<int> AssignMissingLocaleAsync(string key, string locale, CancellationToken cancellationToken = default);

    Task AddAsync(CollectionItem item, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}