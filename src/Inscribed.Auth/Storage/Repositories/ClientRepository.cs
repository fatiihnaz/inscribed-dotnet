using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inscribed.Auth.Storage.Repositories;

public interface IClientRepository
{
    Task<Client?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default);
    void Add(Client client);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

internal sealed class ClientRepository : IClientRepository
{
    private readonly AuthDbContext _context;

    public ClientRepository(AuthDbContext context)
    {
        _context = context;
    }

    public Task<Client?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        _context.Clients.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.Clients.OrderBy(x => x.Key).ToListAsync(cancellationToken);

    public void Add(Client client) => _context.Clients.Add(client);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}