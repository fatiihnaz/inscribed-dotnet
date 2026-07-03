using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inscribed.Auth.Entities;
using Inscribed.Auth.Storage.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Services;

internal interface IGoogleLoginService
{
    Task<string?> StartAsync(string clientKey, string redirectUri, CancellationToken cancellationToken = default);
    Task<LoginCompletion?> CompleteAsync(string state, string code, CancellationToken cancellationToken = default);
}

internal sealed record LoginCompletion(string RedirectUri, string RefreshToken, DateTime RefreshExpiresAtUtc);

internal sealed class GoogleLoginService : IGoogleLoginService
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    private readonly IGoogleOAuthClient _google;
    private readonly IClientRepository _clients;
    private readonly IUserRepository _users;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IDistributedCache _cache;

    public GoogleLoginService(
        IGoogleOAuthClient google,
        IClientRepository clients,
        IUserRepository users,
        IRefreshTokenService refreshTokens,
        IDistributedCache cache)
    {
        _google = google;
        _clients = clients;
        _users = users;
        _refreshTokens = refreshTokens;
        _cache = cache;
    }

    public async Task<string?> StartAsync(string clientKey, string redirectUri, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByKeyAsync(clientKey, cancellationToken);
        if (client is null || !client.IsActive || !IsAllowedRedirect(client, redirectUri))
        {
            return null;
        }

        var state = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var codeVerifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        var payload = JsonSerializer.Serialize(new LoginState(clientKey, redirectUri, codeVerifier));
        await _cache.SetStringAsync(
            CacheKey(state),
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StateTtl },
            cancellationToken);

        return _google.BuildAuthorizationUrl(state, codeChallenge);
    }

    public async Task<LoginCompletion?> CompleteAsync(string state, string code, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(state);
        var payload = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (payload is null)
        {
            return null;
        }

        await _cache.RemoveAsync(cacheKey, cancellationToken);

        var login = JsonSerializer.Deserialize<LoginState>(payload);
        if (login is null)
        {
            return null;
        }

        var googleUser = await _google.ExchangeCodeAsync(code, login.CodeVerifier, cancellationToken);
        if (googleUser is null || !googleUser.EmailVerified)
        {
            return null;
        }

        var email = googleUser.Email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        var user = await _users.GetByGoogleSubjectAsync(googleUser.Subject, cancellationToken)
            ?? await _users.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            user = User.Create(email, googleUser.DisplayName, now, googleUser.Subject);
            _users.Add(user);
        }
        else if (user.GoogleSubject is null)
        {
            user.LinkGoogle(googleUser.Subject, now);
        }
        else if (!string.Equals(user.GoogleSubject, googleUser.Subject, StringComparison.Ordinal))
        {
            return null;
        }

        if (!user.IsActive)
        {
            return null;
        }

        await _users.SaveChangesAsync(cancellationToken);

        var issued = await _refreshTokens.IssueAsync(user.Id, login.ClientKey, cancellationToken);
        return new LoginCompletion(login.RedirectUri, issued.RawToken, issued.ExpiresAtUtc);
    }

    private static string CacheKey(string state) => $"auth:login:{state}";

    private static bool IsAllowedRedirect(Client client, string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);
        return client.AllowedRedirectOrigins.Any(allowed =>
            Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri)
            && string.Equals(allowedUri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record LoginState(string ClientKey, string RedirectUri, string CodeVerifier);
}