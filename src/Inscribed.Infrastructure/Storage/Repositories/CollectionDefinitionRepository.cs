using Microsoft.EntityFrameworkCore;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Repositories;

internal sealed class CollectionDefinitionRepository : ICollectionDefinitionRepository
{
    private readonly CmsDbContext _context;

    public CollectionDefinitionRepository(CmsDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CollectionDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CollectionDefinitions
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionDefinitionStamp>> ListStampsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CollectionDefinitions
            .AsNoTracking()
            .OrderBy(x => x.Key)
            .Select(x => new CollectionDefinitionStamp(x.Key, x.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<int?> GetVersionAsync(string key, CancellationToken cancellationToken = default)
    {
        var versions = await _context.CollectionDefinitions
            .AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => x.Version)
            .ToListAsync(cancellationToken);

        return versions.Count == 0 ? null : versions[0];
    }

    public Task<CollectionDefinition?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return _context.CollectionDefinitions.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return _context.CollectionDefinitions.AnyAsync(cancellationToken);
    }

    public Task AddAsync(CollectionDefinition definition, CancellationToken cancellationToken = default)
    {
        return _context.CollectionDefinitions.AddAsync(definition, cancellationToken).AsTask();
    }

    public void Remove(CollectionDefinition definition)
    {
        _context.CollectionDefinitions.Remove(definition);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
