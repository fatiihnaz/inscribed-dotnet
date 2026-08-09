using Microsoft.EntityFrameworkCore;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;

namespace Inscribed.Infrastructure.Storage.Repositories;

internal sealed class ClientRepository : IClientRepository
{
    private readonly CmsDbContext _context;

    public ClientRepository(CmsDbContext context)
    {
        _context = context;
    }

    public Task<Client?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        _context.Clients.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.Clients.OrderBy(x => x.Key).ToListAsync(cancellationToken);

    public Task AddAsync(Client client, CancellationToken cancellationToken = default) =>
        _context.Clients.AddAsync(client, cancellationToken).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
