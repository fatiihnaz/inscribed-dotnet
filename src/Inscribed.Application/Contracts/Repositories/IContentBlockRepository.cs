using Inscribed.Domain.Entities;

namespace Inscribed.Application.Contracts.Repositories;

public interface IContentBlockRepository
{
    Task<IReadOnlyList<ContentBlock>> GetBySlugAsync(string clientId, string slug, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContentBlock>> GetByClientAsync(string clientId, bool includeArchived = false, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<ContentBlock> blocks, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}