using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Skylab.Cms.Application.Contracts.Services;

namespace Skylab.Cms.Infrastructure.Cache;

public sealed class RedisDraftService : IDraftService
{
    private static readonly TimeSpan DraftTtl = TimeSpan.FromHours(48);

    private readonly IDistributedCache _cache;

    public RedisDraftService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task SaveDraftAsync(string clientId, string slug, IReadOnlyList<DraftBlock> blocks, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(clientId, slug);
        var json = JsonSerializer.Serialize(blocks);
        await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DraftTtl
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<DraftBlock>?> GetDraftAsync(string clientId, string slug, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(clientId, slug);
        var json = await _cache.GetStringAsync(key, cancellationToken);
        if (json is null) return null;
        return JsonSerializer.Deserialize<List<DraftBlock>>(json);
    }

    public async Task DeleteDraftAsync(string clientId, string slug, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(clientId, slug);
        await _cache.RemoveAsync(key, cancellationToken);
    }

    private static string BuildKey(string clientId, string slug) => $"draft:{clientId}:{slug}";
}