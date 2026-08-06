using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Contracts.Responses;
using Inscribed.Application.Contracts.Services;
using Inscribed.Application.Services.Helpers;
using Inscribed.Domain.Entities;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Application.Services;

public sealed class ContentService : IContentService
{
    private readonly IContentBlockRepository _repository;
    private readonly IDraftService _draftService;

    public ContentService(IContentBlockRepository repository, IDraftService draftService)
    {
        _repository = repository;
        _draftService = draftService;
    }

    public async Task<ContentResponse> GetBySlugAsync(string clientId, string? locale, string userId, string slug, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(slug);

        var blocksTask = _repository.GetBySlugAsync(clientId, locale, normalizedSlug, cancellationToken: cancellationToken);
        var draftTask = _draftService.GetDraftAsync(clientId, locale, userId, normalizedSlug, cancellationToken);

        await Task.WhenAll(blocksTask, draftTask);

        var blocks = blocksTask.Result;
        var draft = draftTask.Result;

        var draftLookup = draft?.ToDictionary(d => d.BlockPath, d => d.Value);

        var blockResponses = blocks
            .Select(block =>
            {
                JsonNode? draftValue = null;
                if (draftLookup is not null
                    && draftLookup.TryGetValue(block.BlockPath, out var overlayValue)
                    && !JsonNode.DeepEquals(overlayValue, block.Value))
                {
                    draftValue = overlayValue;
                }

                return new BlockResponse(
                    BlockPath: block.BlockPath,
                    BlockType: block.BlockType.ToString(),
                    Value: block.Value,
                    SortOrder: block.SortOrder,
                    Version: block.Version,
                    Data: null,
                    DraftValue: draftValue
                );
            }).ToList();

        return new ContentResponse(normalizedSlug, locale, blockResponses);
    }

    public async Task<ContentResponse> GetDataBySlugAsync(string clientId, string? locale, string slug, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(slug);

        var blocks = await _repository.GetBySlugAsync(clientId, locale, normalizedSlug, cancellationToken: cancellationToken);

        var blockResponses = blocks
            .Select(block => new BlockResponse(
                BlockPath: block.BlockPath,
                BlockType: block.BlockType.ToString(),
                Value: block.Value,
                SortOrder: block.SortOrder,
                Version: block.Version,
                Data: null))
            .ToList();

        return new ContentResponse(normalizedSlug, locale, blockResponses);
    }

    public async Task<UpdatePageResponse> UpdatePageAsync(string clientId, string? locale, UpdatePageRequest request, string updatedBy, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(request.Slug);

        var blocks = await _repository.GetBySlugAsync(clientId, locale, normalizedSlug, cancellationToken: cancellationToken);
        var blocksByPath = blocks.ToDictionary(b => b.BlockPath);

        var unchanged = 0;
        var pending = new List<(ContentBlock Block, JsonNode Value)>();
        var missingVersions = new List<string>();
        var conflicts = new List<VersionConflict>();

        foreach (var item in request.Blocks)
        {
            var blockPath = SlugNormalizer.NormalizeBlockPath(item.BlockPath);

            if (!blocksByPath.TryGetValue(blockPath, out var block))
                throw new NotFoundException($"Block '{blockPath}' not found for slug '{normalizedSlug}'.");

            if (JsonNode.DeepEquals(block.Value, item.Value))
            {
                unchanged++; continue;
            }

            if (item.Version is not { } version)
            {
                missingVersions.Add($"Block '{blockPath}' changed but carries no version.");
                continue;
            }

            if (version != block.Version)
            {
                conflicts.Add(new VersionConflict(blockPath, block.Version, version));
                continue;
            }

            pending.Add((block, item.Value));
        }

        if (missingVersions.Count > 0)
            throw new ValidationException(missingVersions);

        if (conflicts.Count > 0)
            throw new ConcurrencyConflictException(
                $"Version conflict on slug '{normalizedSlug}': {string.Join("; ", conflicts.Select(c => $"'{c.Path}' expected {c.Expected}, got {c.Provided}"))}.",
                conflicts);

        var utcNow = DateTime.UtcNow;

        foreach (var (block, value) in pending)
            block.UpdateValue(value, updatedBy, utcNow);

        if (pending.Count > 0)
            await _repository.SaveChangesAsync(cancellationToken);

        await _draftService.DeleteDraftAsync(clientId, locale, updatedBy, normalizedSlug, cancellationToken);

        return new UpdatePageResponse(pending.Count, unchanged);
    }

    public async Task<SyncResultResponse> SyncAsync(string clientId, IReadOnlyList<string> locales, IReadOnlyList<SyncManifestRequest> manifests, string syncedBy, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        var targetLocales = locales.Count == 0 ? [null] : locales.Select(l => (string?)l).ToArray();

        var desiredByKey = new Dictionary<(string? Locale, string Slug, string BlockPath), ManifestBlockItem>();
        var manifestPaths = new HashSet<(string Slug, string BlockPath)>();
        var requestSlugs = new HashSet<string>();

        foreach (var manifest in manifests)
        {
            var slug = SlugNormalizer.NormalizeSlug(manifest.Slug);
            requestSlugs.Add(slug);

            foreach (var item in manifest.Blocks)
            {
                var blockPath = SlugNormalizer.NormalizeBlockPath(item.BlockPath);
                manifestPaths.Add((slug, blockPath));

                foreach (var locale in targetLocales)
                    desiredByKey[(locale, slug, blockPath)] = item;
            }
        }

        var existing = await _repository.GetByClientAsync(clientId, includeArchived: true, cancellationToken: cancellationToken);

        var existingByKey = new Dictionary<(string? Locale, string Slug, string BlockPath), ContentBlock>();
        foreach (var block in existing)
            existingByKey[(block.Locale, block.Slug, block.BlockPath)] = block;

        if (locales.Count > 0)
        {
            var defaultLocale = locales[0];

            foreach (var block in existing)
            {
                if (block.Locale is not null || !manifestPaths.Contains((block.Slug, block.BlockPath)))
                    continue;

                (string? Locale, string Slug, string BlockPath) target = (defaultLocale, block.Slug, block.BlockPath);
                if (existingByKey.ContainsKey(target))
                    continue;

                if (!block.AdoptLocale(defaultLocale, syncedBy, utcNow))
                    continue;

                existingByKey.Remove((null, block.Slug, block.BlockPath));
                existingByKey[target] = block;
            }
        }

        var counts = requestSlugs.ToDictionary(slug => slug, _ => new SlugCounts());
        var prunedSlugs = new HashSet<string>();

        var toCreate = new List<ContentBlock>();

        foreach (var block in existing)
        {
            var key = (block.Locale, block.Slug, block.BlockPath);

            if (desiredByKey.TryGetValue(key, out var item))
            {
                if (block.IsArchived)
                {
                    block.Restore(syncedBy, utcNow);
                    block.UpdateDefinition(item.BlockType, item.SortOrder, syncedBy, utcNow);
                    counts[block.Slug].Restored.Add(block.BlockPath);
                }
                else
                {
                    block.UpdateDefinition(item.BlockType, item.SortOrder, syncedBy, utcNow);
                    counts[block.Slug].Unchanged.Add(block.BlockPath);
                }
            }
            else if (!block.IsArchived)
            {
                block.Archive(syncedBy, utcNow);

                if (requestSlugs.Contains(block.Slug))
                    counts[block.Slug].Deleted.Add(block.BlockPath);
                else
                    prunedSlugs.Add(block.Slug);
            }
        }

        foreach (var ((locale, slug, blockPath), item) in desiredByKey)
        {
            if (existingByKey.ContainsKey((locale, slug, blockPath)))
                continue;

            toCreate.Add(ContentBlock.Create(
                clientId,
                locale,
                slug,
                blockPath,
                item.BlockType,
                item.DefaultValue,
                item.SortOrder,
                syncedBy,
                utcNow
            ));
            counts[slug].Created.Add(blockPath);
        }

        if (toCreate.Count > 0)
            await _repository.AddRangeAsync(toCreate, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        var results = counts
            .Select(kvp => new SyncSlugResult(
                kvp.Key,
                kvp.Value.Created.Count,
                kvp.Value.Deleted.Count,
                kvp.Value.Unchanged.Count,
                kvp.Value.Restored.Count))
            .ToList();

        return new SyncResultResponse(results, prunedSlugs.ToList());
    }

    private sealed class SlugCounts
    {
        public readonly HashSet<string> Created = new(StringComparer.Ordinal);
        public readonly HashSet<string> Deleted = new(StringComparer.Ordinal);
        public readonly HashSet<string> Unchanged = new(StringComparer.Ordinal);
        public readonly HashSet<string> Restored = new(StringComparer.Ordinal);
    }

    public async Task SaveDraftAsync(string clientId, string? locale, string userId, UpdatePageRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(request.Slug);

        var existing = await _draftService.GetDraftAsync(clientId, locale, userId, normalizedSlug, cancellationToken);

        var overlay = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        if (existing is not null)
        {
            foreach (var block in existing)
                overlay[block.BlockPath] = block.Value;
        }

        foreach (var item in request.Blocks)
            overlay[SlugNormalizer.NormalizeBlockPath(item.BlockPath)] = item.Value;

        var draftBlocks = overlay
            .Select(entry => new DraftBlock(entry.Key, entry.Value))
            .ToList();

        await _draftService.SaveDraftAsync(clientId, locale, userId, normalizedSlug, draftBlocks, cancellationToken);
    }

    public Task DiscardDraftAsync(string clientId, string? locale, string userId, string slug, CancellationToken cancellationToken = default)
        => _draftService.DeleteDraftAsync(clientId, locale, userId, SlugNormalizer.NormalizeSlug(slug), cancellationToken);
}
