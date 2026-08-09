using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inscribed.Auth.Storage.Repositories;

public interface IClientIdentityRepository
{
    Task<ClientIdentity?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClientIdentity>> GetAllAsync(CancellationToken cancellationToken = default);
    void Add(ClientIdentity client);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

internal sealed class ClientIdentityRepository : IClientIdentityRepository
{
    private readonly AuthDbContext _context;

    public ClientIdentityRepository(AuthDbContext context)
    {
        _context = context;
    }

    public Task<ClientIdentity?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        _context.Clients.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

    public async Task<IReadOnlyList<ClientIdentity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.Clients.OrderBy(x => x.Key).ToListAsync(cancellationToken);

    public void Add(ClientIdentity client) => _context.Clients.Add(client);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
