using System.Security.Cryptography;
using Inscribed.Auth.Entities;
using Inscribed.Auth.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Inscribed.Auth.Services;

internal sealed class SigningKeyStore : ISigningKeyStore
{
    private const string Algorithm = "RS256";
    private const int RsaKeySize = 2048;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinReloadInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetiredKeyGrace = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _gate = new();
    private volatile State? _state;

    public SigningKeyStore(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public SigningCredentials GetActiveSigningCredentials() => Load().ActiveCredentials;

    public IEnumerable<SecurityKey> GetValidationKeys(string? kid = null)
    {
        var state = Load();
        if (kid is null)
        {
            return state.ValidationKeys;
        }

        var matches = Filter(state, kid);
        return matches.Count > 0 ? matches : Filter(Reload(), kid);
    }

    public IReadOnlyList<JwksKey> GetPublicJwks() => Load().Jwks;

    public string Rotate()
    {
        lock (_gate)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            var now = DateTime.UtcNow;
            foreach (var key in db.SigningKeys.Where(k => k.IsActive).ToList())
            {
                key.Deactivate(now, now + RetiredKeyGrace);
            }

            var generated = Generate();
            db.SigningKeys.Add(generated);
            db.SaveChanges();

            _state = null;
            return generated.Kid;
        }
    }

    private static List<SecurityKey> Filter(State state, string kid) =>
        state.ValidationKeys.Where(k => string.Equals(k.KeyId, kid, StringComparison.Ordinal)).ToList();

    private State Load()
    {
        if (_state is { } cached && cached.LoadedAtUtc > DateTime.UtcNow - CacheTtl)
        {
            return cached;
        }

        lock (_gate)
        {
            if (_state is { } current && current.LoadedAtUtc > DateTime.UtcNow - CacheTtl)
            {
                return current;
            }

            var built = Build();
            _state = built;
            return built;
        }
    }

    private State Reload()
    {
        lock (_gate)
        {
            if (_state is { } current && current.LoadedAtUtc > DateTime.UtcNow - MinReloadInterval)
            {
                return current;
            }

            var built = Build();
            _state = built;
            return built;
        }
    }

    private State Build()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var now = DateTime.UtcNow;
        var keys = db.SigningKeys.Where(k => k.ExpiresAt == null || k.ExpiresAt > now).OrderByDescending(k => k.CreatedAt).ToList();

        if (!keys.Any(k => k.IsActive))
        {
            var generated = Generate();
            db.SigningKeys.Add(generated);
            db.SaveChanges();
            keys.Insert(0, generated);
        }

        var active = keys.First(k => k.IsActive);

        var validationKeys = new List<SecurityKey>(keys.Count);
        var jwks = new List<JwksKey>(keys.Count);
        SigningCredentials? activeCredentials = null;

        foreach (var key in keys)
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(key.Id == active.Id ? key.PrivateKeyPem : key.PublicKeyPem);

            var securityKey = new RsaSecurityKey(rsa) { KeyId = key.Kid };
            validationKeys.Add(securityKey);

            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(securityKey);
            jwks.Add(new JwksKey("RSA", "sig", Algorithm, key.Kid, jwk.N, jwk.E));

            if (key.Id == active.Id)
            {
                activeCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
            }
        }

        return new State(activeCredentials!, validationKeys, jwks, DateTime.UtcNow);
    }

    private static SigningKey Generate()
    {
        using var rsa = RSA.Create(RsaKeySize);
        var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();
        var kid = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));

        return SigningKey.Create(kid, Algorithm, publicPem, privatePem, DateTime.UtcNow);
    }

    private sealed record State(
        SigningCredentials ActiveCredentials,
        IReadOnlyList<SecurityKey> ValidationKeys,
        IReadOnlyList<JwksKey> Jwks,
        DateTime LoadedAtUtc);
}
