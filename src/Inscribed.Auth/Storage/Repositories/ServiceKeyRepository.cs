using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inscribed.Auth.Storage.Repositories;

public interface IServiceKeyRepository
{
    Task<IReadOnlyList<ServiceKey>> GetByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceKey>> GetByClientKeyAsync(string clientKey, CancellationToken cancellationToken = default);
    Task<ServiceKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(ServiceKey key);
    Task TouchLastUsedAsync(Guid id, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

internal sealed class ServiceKeyRepository : IServiceKeyRepository
{
    private readonly AuthDbContext _context;

    public ServiceKeyRepository(AuthDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ServiceKey>> GetByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default) =>
        await _context.ServiceKeys.Where(x => x.KeyPrefix == keyPrefix).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ServiceKey>> GetByClientKeyAsync(string clientKey, CancellationToken cancellationToken = default) =>
        await _context.ServiceKeys.Where(x => x.ClientKey == clientKey).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public Task<ServiceKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.ServiceKeys.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(ServiceKey key) => _context.ServiceKeys.Add(key);

    public Task TouchLastUsedAsync(Guid id, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var threshold = utcNow.AddMinutes(-1);
        return _context.ServiceKeys
            .Where(x => x.Id == id && (x.LastUsedAt == null || x.LastUsedAt < threshold))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastUsedAt, utcNow), cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
