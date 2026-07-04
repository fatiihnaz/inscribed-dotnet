using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inscribed.Auth.Storage.Repositories;

public interface IMembershipRepository
{
    Task<Membership?> GetAsync(Guid userId, Guid clientId, CancellationToken cancellationToken = default);
    void Add(Membership membership);
    void Remove(Membership membership);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

internal sealed class MembershipRepository : IMembershipRepository
{
    private readonly AuthDbContext _context;

    public MembershipRepository(AuthDbContext context)
    {
        _context = context;
    }

    public Task<Membership?> GetAsync(Guid userId, Guid clientId, CancellationToken cancellationToken = default) =>
        _context.Memberships.FirstOrDefaultAsync(x => x.UserId == userId && x.ClientId == clientId, cancellationToken);

    public void Add(Membership membership) => _context.Memberships.Add(membership);

    public void Remove(Membership membership) => _context.Memberships.Remove(membership);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}