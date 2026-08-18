using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Repositories;

internal sealed class CollectionItemRepository : ICollectionItemRepository
{
    private readonly CmsDbContext _context;

    public CollectionItemRepository(CmsDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CollectionItem>> ListAsync(string key, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = _context.CollectionItems.AsQueryable();

        if (includeArchived)
            query = query.IgnoreQueryFilters();

        return await query
            .Where(x => x.CollectionKey == key)
            .OrderBy(x => x.Slug)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<CollectionItem> Items, int Total)> ListPagedAsync(
        string key,
        string? locale,
        JsonObject? filterContainment,
        CollectionSort sort,
        bool archived,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = archived
            ? _context.CollectionItems.IgnoreQueryFilters().Where(x => x.IsArchived)
            : _context.CollectionItems.AsQueryable();

        query = query.Where(x => x.CollectionKey == key);

        if (locale is not null)
            query = query.Where(x => x.Locale == locale);

        var filterJson = filterContainment is { Count: > 0 } ? filterContainment.ToJsonString() : null;

        if (filterJson is not null)
            query = query.Where(x => EF.Functions.JsonContains(x.Data, filterJson));

        var total = await query.CountAsync(cancellationToken);
        var items = await Order(query, sort).Skip(offset).Take(limit).ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<CollectionItem?> GetBySlugAsync(string key, string slug, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = _context.CollectionItems.AsQueryable();

        if (includeArchived)
            query = query.IgnoreQueryFilters();

        return await query.FirstOrDefaultAsync(x => x.CollectionKey == key && x.Slug == slug, cancellationToken);
    }

    public async Task<IReadOnlyList<TakenSlug>> GetTakenSlugsAsync(string key, IReadOnlyCollection<string> slugs, CancellationToken cancellationToken = default)
    {
        if (slugs.Count == 0)
            return [];

        return await _context.CollectionItems
            .IgnoreQueryFilters()
            .Where(x => x.CollectionKey == key && slugs.Contains(x.Slug))
            .Select(x => new TakenSlug(x.Slug, x.IsArchived, x.Version, x.TranslationGroupId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionItem>> GetBySlugsAsync(string key, IReadOnlyCollection<string> slugs, CancellationToken cancellationToken = default)
    {
        if (slugs.Count == 0)
            return [];

        return await _context.CollectionItems
            .IgnoreQueryFilters()
            .Where(x => x.CollectionKey == key && slugs.Contains(x.Slug))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionItem>> GetByTranslationGroupAsync(string key, Guid translationGroupId, CancellationToken cancellationToken = default)
    {
        return await _context.CollectionItems
            .Where(x => x.CollectionKey == key && x.TranslationGroupId == translationGroupId)
            .OrderBy(x => x.Slug)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionItem>> GetByTranslationGroupsAsync(string key, IReadOnlyCollection<Guid> translationGroupIds, CancellationToken cancellationToken = default)
    {
        if (translationGroupIds.Count == 0)
            return [];

        return await _context.CollectionItems
            .Where(x => x.CollectionKey == key && translationGroupIds.Contains(x.TranslationGroupId))
            .OrderBy(x => x.Slug)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(string key, CancellationToken cancellationToken = default)
    {
        return _context.CollectionItems
            .IgnoreQueryFilters()
            .CountAsync(x => x.CollectionKey == key, cancellationToken);
    }

    public Task<int> AssignMissingLocaleAsync(string key, string locale, CancellationToken cancellationToken = default)
    {
        return _context.CollectionItems
            .IgnoreQueryFilters()
            .Where(x => x.CollectionKey == key && x.Locale == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Locale, locale), cancellationToken);
    }

    public Task AddAsync(CollectionItem item, CancellationToken cancellationToken = default)
    {
        return _context.CollectionItems.AddAsync(item, cancellationToken).AsTask();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<CollectionItem> Order(IQueryable<CollectionItem> query, CollectionSort sort)
    {
        if (sort.Field == CollectionSortField.DataField)
            return OrderByDataField(query, sort.DataField ?? throw new InvalidOperationException("A data-field sort must name a field."), sort.Descending);

        return (sort.Field, sort.Descending) switch
        {
            (CollectionSortField.CreatedAt, false) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Slug),
            (CollectionSortField.CreatedAt, true) => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Slug),
            (CollectionSortField.UpdatedAt, false) => query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Slug),
            (CollectionSortField.UpdatedAt, true) => query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Slug),
            (_, true) => query.OrderByDescending(x => x.Slug),
            _ => query.OrderBy(x => x.Slug),
        };
    }

    private static IQueryable<CollectionItem> OrderByDataField(IQueryable<CollectionItem> query, string field, bool descending)
    {
        var ordered = query.OrderBy(x => CmsDbFunctions.JsonValue(x.Data, field) == null);

        return (descending
                ? ordered.ThenByDescending(x => CmsDbFunctions.JsonValue(x.Data, field))
                : ordered.ThenBy(x => CmsDbFunctions.JsonValue(x.Data, field)))
            .ThenBy(x => x.Slug);
    }
}
