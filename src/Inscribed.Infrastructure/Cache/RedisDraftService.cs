using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Inscribed.Application.Contracts.Services;

namespace Inscribed.Infrastructure.Cache;

public sealed class RedisDraftService : IDraftService
{
    private static readonly TimeSpan DraftTtl = TimeSpan.FromHours(48);

    private readonly IDistributedCache _cache;
    private readonly RedisKeyScanner _scanner;

    public RedisDraftService(IDistributedCache cache, RedisKeyScanner scanner)
    {
        _cache = cache;
        _scanner = scanner;
    }

    public async Task SaveDraftAsync(string clientId, string? locale, string userId, string slug, IReadOnlyList<DraftBlock> blocks, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(clientId, locale, userId, slug);
        var json = JsonSerializer.Serialize(blocks);
        await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DraftTtl
        }, cancellationToken);
    }

    public Task<IReadOnlyList<DraftBlock>?> GetDraftAsync(string clientId, string? locale, string userId, string slug, CancellationToken cancellationToken = default)
        => ReadAsync(BuildKey(clientId, locale, userId, slug), cancellationToken);

    public async Task<IReadOnlySet<string>> GetDraftedBlockPathsAsync(string clientId, string? locale, string slug, CancellationToken cancellationToken = default)
    {
        var pattern = BuildKey(RedisKeyScanner.Escape(clientId), RedisKeyScanner.Escape(locale ?? "_"), "*", RedisKeyScanner.Escape(slug));
        var blockPaths = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var key in _scanner.ScanAsync(pattern, cancellationToken))
        {
            if (await ReadAsync(key, cancellationToken) is { } draft)
                blockPaths.UnionWith(draft.Select(block => block.BlockPath));
        }

        return blockPaths;
    }

    public async Task DeleteDraftAsync(string clientId, string? locale, string userId, string slug, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(clientId, locale, userId, slug);
        await _cache.RemoveAsync(key, cancellationToken);
    }

    private async Task<IReadOnlyList<DraftBlock>?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var json = await _cache.GetStringAsync(key, cancellationToken);
        if (json is null) return null;
        return JsonSerializer.Deserialize<List<DraftBlock>>(json);
    }

    private static string BuildKey(string clientId, string? locale, string userId, string slug) => $"draft:{clientId}:{locale ?? "_"}:{userId}:{slug}";
}
