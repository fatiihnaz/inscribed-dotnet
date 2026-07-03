using Inscribed.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inscribed.Auth.Storage.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    void Add(RefreshToken token);
    Task RevokeFamilyAsync(Guid familyId, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

internal sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AuthDbContext _context;

    public RefreshTokenRepository(AuthDbContext context)
    {
        _context = context;
    }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _context.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public void Add(RefreshToken token) => _context.RefreshTokens.Add(token);

    public Task RevokeFamilyAsync(Guid familyId, DateTime utcNow, CancellationToken cancellationToken = default) =>
        _context.RefreshTokens
            .Where(x => x.FamilyId == familyId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAt, utcNow)
                .SetProperty(x => x.UpdatedAt, utcNow), cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}