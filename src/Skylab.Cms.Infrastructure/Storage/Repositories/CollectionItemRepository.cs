using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Skylab.Cms.Application.Contracts.Repositories;
using Skylab.Cms.Domain.Entities;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Infrastructure.Storage.Repositories;

internal sealed class CollectionItemRepository : ICollectionItemRepository
{
    private readonly CmsDbContext _context;

    public CollectionItemRepository(CmsDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CollectionItem>> ListAsync(CollectionKey key, bool includeArchived = false, CancellationToken cancellationToken = default)
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
        CollectionKey key,
        JsonObject? filterContainment,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CollectionItems.AsQueryable().Where(x => x.CollectionKey == key);

        if (filterContainment is { Count: > 0 })
        {
            var filterJson = filterContainment.ToJsonString();
            query = query.Where(x => EF.Functions.JsonContains(x.Data, filterJson));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(x => x.Slug)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<CollectionItem?> GetBySlugAsync(CollectionKey key, string slug, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = _context.CollectionItems.AsQueryable();

        if (includeArchived)
            query = query.IgnoreQueryFilters();

        return await query.FirstOrDefaultAsync(x => x.CollectionKey == key && x.Slug == slug, cancellationToken);
    }

    public Task AddAsync(CollectionItem item, CancellationToken cancellationToken = default)
    {
        return _context.CollectionItems.AddAsync(item, cancellationToken).AsTask();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}