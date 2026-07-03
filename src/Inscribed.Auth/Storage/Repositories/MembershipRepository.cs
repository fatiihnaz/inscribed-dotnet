using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inscribed.Auth.Storage.Repositories;

public interface IMembershipRepository
{
    Task<Membership?> GetAsync(Guid userId, Guid clientId, CancellationToken cancellationToken = default);
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
}