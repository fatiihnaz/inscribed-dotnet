using Microsoft.EntityFrameworkCore;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Repositories;

internal sealed class CollectionSlugAliasRepository : ICollectionSlugAliasRepository
{
    private readonly CmsDbContext _context;

    public CollectionSlugAliasRepository(CmsDbContext context)
    {
        _context = context;
    }

    public Task<CollectionSlugAlias?> GetAsync(string key, string slug, CancellationToken cancellationToken = default)
    {
        return _context.CollectionSlugAliases
            .FirstOrDefaultAsync(x => x.CollectionKey == key && x.Slug == slug, cancellationToken);
    }

    public Task<bool> ExistsAsync(string key, string slug, CancellationToken cancellationToken = default)
    {
        return _context.CollectionSlugAliases
            .AnyAsync(x => x.CollectionKey == key && x.Slug == slug, cancellationToken);
    }

    public Task AddAsync(CollectionSlugAlias alias, CancellationToken cancellationToken = default)
    {
        return _context.CollectionSlugAliases.AddAsync(alias, cancellationToken).AsTask();
    }

    public void Remove(CollectionSlugAlias alias)
    {
        _context.CollectionSlugAliases.Remove(alias);
    }
}
