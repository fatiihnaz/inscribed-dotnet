using System.Linq.Expressions;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Repositories;

internal sealed class CollectionItemRepository : ICollectionItemRepository
{
    private const float SimilarityThreshold = 0.5f;

    private const int MinSimilarPhraseLength = 3;

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

    public async Task<(IReadOnlyList<CollectionItem> Items, int Total, bool Approximate)> ListPagedAsync(
        string key,
        string? locale,
        JsonObject? filterContainment,
        CollectionSearch? search,
        string? displayField,
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

        if (search is null)
        {
            var total = await query.CountAsync(cancellationToken);
            var items = await Order(query, sort).Skip(offset).Take(limit).ToListAsync(cancellationToken);

            return (items, total, false);
        }

        var matched = Match(query, search, displayField);
        var matchedTotal = await matched.CountAsync(cancellationToken);

        if (matchedTotal > 0)
        {
            var items = await Order(matched, sort, Rank(search.Phrase, displayField)).Skip(offset).Take(limit).ToListAsync(cancellationToken);

            return (items, matchedTotal, false);
        }

        if (search.Phrase.Length < MinSimilarPhraseLength)
            return ([], 0, false);

        var phrase = search.Phrase;
        var similar = query
            .Select(x => new
            {
                Item = x,
                Score = CmsDbFunctions.WordSimilarity(CmsDbFunctions.Fold(phrase), CmsDbFunctions.Fold(CmsDbFunctions.JsonText(x.Data, displayField) ?? x.Slug)),
            })
            .Where(x => x.Score >= SimilarityThreshold);

        var similarTotal = await similar.CountAsync(cancellationToken);
        var similarItems = await similar
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Slug)
            .Skip(offset)
            .Take(limit)
            .Select(x => x.Item)
            .ToListAsync(cancellationToken);

        return (similarItems, similarTotal, similarTotal > 0);
    }

    public async Task<(IReadOnlyList<CollectionItem> Items, int Total)> LookupAsync(
        string key,
        string? locale,
        string? displayField,
        CollectionSearch? search,
        IReadOnlyCollection<string>? slugs,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CollectionItems.Where(x => x.CollectionKey == key);

        if (slugs is { Count: > 0 })
        {
            query = query.Where(x => slugs.Contains(x.Slug));
        }
        else
        {
            if (locale is not null)
                query = query.Where(x => x.Locale == locale);

            if (search is not null)
                query = Match(query, search, displayField);
        }

        var total = await query.CountAsync(cancellationToken);

        var ranked = search is null ? null : query.OrderBy(Rank(search.Phrase, displayField));
        var ordered = displayField is null
            ? By(query, ranked, x => x.Slug)
            : By(query, ranked, x => CmsDbFunctions.JsonText(x.Data, displayField)).ThenBy(x => x.Slug);

        var items = await ordered.Take(limit).ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<int> CountByContainmentAsync(string key, JsonObject containment, CancellationToken cancellationToken = default)
    {
        var json = containment.ToJsonString();

        return _context.CollectionItems
            .Where(x => x.CollectionKey == key && EF.Functions.JsonContains(x.Data, json))
            .CountAsync(cancellationToken);
    }

    public async Task<CollectionItem?> GetBySlugAsync(string key, string slug, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = _context.CollectionItems.AsQueryable();

        if (includeArchived)
            query = query.IgnoreQueryFilters();

        return await query.FirstOrDefaultAsync(x => x.CollectionKey == key && x.Slug == slug, cancellationToken);
    }

    public async Task<CollectionItem?> GetByIdAsync(string key, Guid id, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = _context.CollectionItems.AsQueryable();

        if (includeArchived)
            query = query.IgnoreQueryFilters();

        return await query.FirstOrDefaultAsync(x => x.CollectionKey == key && x.Id == id, cancellationToken);
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

    public async Task<IReadOnlyList<CollectionItemCount>> CountByCollectionAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
            return [];

        return await _context.CollectionItems
            .Where(x => keys.Contains(x.CollectionKey))
            .GroupBy(x => new { x.CollectionKey, x.Locale })
            .Select(group => new CollectionItemCount(group.Key.CollectionKey, group.Key.Locale, group.Count()))
            .ToListAsync(cancellationToken);
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

    private static IQueryable<CollectionItem> Match(IQueryable<CollectionItem> query, CollectionSearch search, string? displayField)
    {
        foreach (var term in search.Terms)
        {
            query = query.Where(x =>
                CmsDbFunctions.Fold(x.Slug)!.Contains(CmsDbFunctions.Fold(term)!)
                || CmsDbFunctions.Fold(CmsDbFunctions.JsonText(x.Data, displayField))!.Contains(CmsDbFunctions.Fold(term)!));
        }

        return query;
    }

    private static Expression<Func<CollectionItem, int>> Rank(string phrase, string? displayField) =>
        x => CmsDbFunctions.Fold(CmsDbFunctions.JsonText(x.Data, displayField) ?? x.Slug) == CmsDbFunctions.Fold(phrase) ? 0
            : CmsDbFunctions.Fold(CmsDbFunctions.JsonText(x.Data, displayField) ?? x.Slug)!.StartsWith(CmsDbFunctions.Fold(phrase)!) ? 1
            : CmsDbFunctions.Fold(" " + (CmsDbFunctions.JsonText(x.Data, displayField) ?? x.Slug))!.Contains(CmsDbFunctions.Fold(" " + phrase)!) ? 2
            : 3;

    private static IOrderedQueryable<CollectionItem> Order(IQueryable<CollectionItem> query, CollectionSort sort, Expression<Func<CollectionItem, int>>? rank = null)
    {
        var ranked = rank is null ? null : query.OrderBy(rank);

        return sort.Field switch
        {
            CollectionSortField.DataField => OrderByDataField(query, ranked, sort.DataField ?? throw new InvalidOperationException("A data-field sort must name a field."), sort.Descending),
            CollectionSortField.CreatedAt => By(query, ranked, x => x.CreatedAt, sort.Descending).ThenBy(x => x.Slug),
            CollectionSortField.UpdatedAt => By(query, ranked, x => x.UpdatedAt, sort.Descending).ThenBy(x => x.Slug),
            _ => By(query, ranked, x => x.Slug, sort.Descending),
        };
    }

    private static IOrderedQueryable<CollectionItem> OrderByDataField(IQueryable<CollectionItem> query, IOrderedQueryable<CollectionItem>? ranked, string field, bool descending)
    {
        var present = By(query, ranked, x => CmsDbFunctions.JsonValue(x.Data, field) == null);

        return By(query, present, x => CmsDbFunctions.JsonValue(x.Data, field), descending).ThenBy(x => x.Slug);
    }

    private static IOrderedQueryable<CollectionItem> By<TKey>(
        IQueryable<CollectionItem> query,
        IOrderedQueryable<CollectionItem>? ordered,
        Expression<Func<CollectionItem, TKey>> key,
        bool descending = false)
    {
        if (ordered is null)
            return descending ? query.OrderByDescending(key) : query.OrderBy(key);

        return descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
    }
}
