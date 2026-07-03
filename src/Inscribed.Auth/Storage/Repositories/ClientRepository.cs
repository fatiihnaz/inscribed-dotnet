using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inscribed.Auth.Storage.Repositories;

public interface IClientRepository
{
    Task<Client?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
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
}