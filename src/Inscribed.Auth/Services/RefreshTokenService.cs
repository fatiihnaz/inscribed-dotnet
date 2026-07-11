using System.Security.Cryptography;
using System.Text;
using Inscribed.Auth.Entities;
using Inscribed.Auth.Options;
using Inscribed.Auth.Storage.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Services;

internal interface IRefreshTokenService
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, string clientKey, CancellationToken cancellationToken = default);
    Task<RefreshResult?> RefreshAsync(string rawToken, CancellationToken cancellationToken = default);
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);
}

internal sealed record IssuedRefreshToken(string RawToken, DateTime ExpiresAtUtc);

internal sealed record RefreshResult(AccessToken AccessToken, string NewRefreshToken, DateTime RefreshExpiresAtUtc);

internal sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUserRepository _users;
    private readonly IClientRepository _clients;
    private readonly IMembershipRepository _memberships;
    private readonly IJwtIssuer _jwtIssuer;
    private readonly AuthOptions _options;

    public RefreshTokenService(
        IRefreshTokenRepository refreshTokens,
        IUserRepository users,
        IClientRepository clients,
        IMembershipRepository memberships,
        IJwtIssuer jwtIssuer,
        IOptions<AuthOptions> options)
    {
        _refreshTokens = refreshTokens;
        _users = users;
        _clients = clients;
        _memberships = memberships;
        _jwtIssuer = jwtIssuer;
        _options = options.Value;
    }

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, string clientKey, CancellationToken cancellationToken = default)
    {
        var (raw, hash) = NewToken();
        var now = DateTime.UtcNow;
        var expires = now.AddDays(_options.RefreshTokenDays);

        _refreshTokens.Add(RefreshToken.Issue(userId, clientKey, hash, expires, now));
        await _refreshTokens.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(raw, expires);
    }

    public async Task<RefreshResult?> RefreshAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var current = await _refreshTokens.GetByHashAsync(Hash(rawToken), cancellationToken);
        if (current is null)
        {
            return null;
        }

        RefreshToken? successor = null;
        if (current.RevokedAt is not null)
        {
            var leeway = TimeSpan.FromSeconds(_options.ReuseLeewaySeconds);
            successor = current.ReplacedByHash is null || current.RevokedAt <= now - leeway
                ? null
                : await _refreshTokens.GetByHashAsync(current.ReplacedByHash, cancellationToken);

            if (successor is null || successor.RevokedAt is not null)
            {
                await _refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
                return null;
            }
        }

        if (current.ExpiresAt <= now)
        {
            return null;
        }

        var user = await _users.GetByIdAsync(current.UserId, cancellationToken);
        var client = await _clients.GetByKeyAsync(current.ClientKey, cancellationToken);
        if (user is null || !user.IsActive || client is null || !client.IsActive)
        {
            return null;
        }

        var roles = await ResolveRolesAsync(user, client, cancellationToken);

        var (raw, hash) = NewToken();
        var expires = now.AddDays(_options.RefreshTokenDays);
        current.Revoke(now, hash);
        successor?.Revoke(now, hash);
        _refreshTokens.Add(RefreshToken.Issue(user.Id, current.ClientKey, hash, expires, now, current.FamilyId));

        try
        {
            await _refreshTokens.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
        var access = _jwtIssuer.Issue(user.Id.ToString(), current.ClientKey, roles, displayName, user.Email);
        return new RefreshResult(access, raw, expires);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var current = await _refreshTokens.GetByHashAsync(Hash(rawToken), cancellationToken);
        if (current is null || current.RevokedAt is not null)
        {
            return;
        }

        current.Revoke(DateTime.UtcNow);
        await _refreshTokens.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ResolveRolesAsync(User user, Client client, CancellationToken cancellationToken)
    {
        var membership = await _memberships.GetAsync(user.Id, client.Id, cancellationToken);
        var roles = new List<string>(membership?.Roles ?? []);

        if (_options.Admin.BootstrapAdmins.Contains(user.Email, StringComparer.OrdinalIgnoreCase)
            && !roles.Contains(_options.Admin.Role))
        {
            roles.Add(_options.Admin.Role);
        }

        return roles;
    }

    private static (string Raw, string Hash) NewToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(rawToken)));
}