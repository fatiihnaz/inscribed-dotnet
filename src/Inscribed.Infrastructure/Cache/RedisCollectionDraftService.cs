using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Distributed;
using Inscribed.Application.Contracts.Services;

namespace Inscribed.Infrastructure.Cache;

public sealed class RedisCollectionDraftService : ICollectionDraftService
{
    private static readonly TimeSpan DraftTtl = TimeSpan.FromDays(7);

    private readonly IDistributedCache _cache;

    public RedisCollectionDraftService(IDistributedCache cache)
    {
        _cache = cache;
    }

    private static string ItemKey(string key, string slug, string userId) => $"cd:item:{key}:{slug}:{userId}";

    private static string NewKey(string key, string? locale, string userId) => $"cd:new:{key}:{locale ?? "_"}:{userId}";

    public async Task SaveItemDraftAsync(string key, string slug, string userId, JsonObject data, CancellationToken cancellationToken = default)
    {
        var payload = new CollectionDraft(slug, data, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(payload);
        await _cache.SetStringAsync(ItemKey(key, slug, userId), json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DraftTtl
        }, cancellationToken);
    }

    public async Task<CollectionDraft?> GetItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(ItemKey(key, slug, userId), cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<CollectionDraft>(json);
    }

    public Task DeleteItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default)
        => _cache.RemoveAsync(ItemKey(key, slug, userId), cancellationToken);

    public async Task SaveNewDraftAsync(string key, string? locale, string userId, string? slug, Guid? translationGroupId, JsonObject data, CancellationToken cancellationToken = default)
    {
        var payload = new CollectionDraft(slug, data, DateTime.UtcNow, translationGroupId);
        var json = JsonSerializer.Serialize(payload);
        await _cache.SetStringAsync(NewKey(key, locale, userId), json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DraftTtl
        }, cancellationToken);
    }

    public async Task<CollectionDraft?> GetNewDraftAsync(string key, string? locale, string userId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(NewKey(key, locale, userId), cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<CollectionDraft>(json);
    }

    public Task DeleteNewDraftAsync(string key, string? locale, string userId, CancellationToken cancellationToken = default)
        => _cache.RemoveAsync(NewKey(key, locale, userId), cancellationToken);
}