using Inscribed.Domain.Entities;

namespace Inscribed.Application.Contracts.Repositories;

public interface IClientRepository
{
    Task<Client?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Client client, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
