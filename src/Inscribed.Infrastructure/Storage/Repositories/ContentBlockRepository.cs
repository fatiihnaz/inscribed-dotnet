using Microsoft.EntityFrameworkCore;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Repositories;

internal sealed class ContentBlockRepository : IContentBlockRepository
{
    private readonly CmsDbContext _context;

    public ContentBlockRepository(CmsDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ContentBlock>> GetBySlugAsync(string clientId, string slug, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = _context.ContentBlocks.AsQueryable();

        if (includeArchived)
        {
            query = query.IgnoreQueryFilters();
        }

        return await query
            .Where(x => x.ClientId == clientId && x.Slug == slug)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public Task AddRangeAsync(IEnumerable<ContentBlock> blocks, CancellationToken cancellationToken = default)
    {
        return _context.ContentBlocks.AddRangeAsync(blocks, cancellationToken);
    }

    public void ArchiveRange(IEnumerable<ContentBlock> blocks)
    {
        _context.ContentBlocks.UpdateRange(blocks);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}