using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Contracts.Responses;
using Inscribed.Application.Contracts.Services;
using Inscribed.Application.Services.Helpers;
using Inscribed.Domain.Entities;
using Inscribed.Domain.Enums;
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

    public async Task<ContentResponse> GetBySlugAsync(string clientId, string userId, string slug, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(slug);

        var blocksTask = _repository.GetBySlugAsync(clientId, normalizedSlug, cancellationToken: cancellationToken);
        var draftTask = _draftService.GetDraftAsync(clientId, userId, normalizedSlug, cancellationToken);

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
                    && overlayValue?.ToJsonString() != block.Value.ToJsonString())
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

        return new ContentResponse(normalizedSlug, blockResponses);
    }

    public async Task<ContentResponse> GetDataBySlugAsync(string clientId, string slug, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(slug);

        var blocks = await _repository.GetBySlugAsync(clientId, normalizedSlug, cancellationToken: cancellationToken);

        var dataBlocks = blocks.Where(block => block.BlockType == BlockType.DataSource)
            .Select(block => new BlockResponse(
                BlockPath: block.BlockPath,
                BlockType: block.BlockType.ToString(),
                Value: block.Value,
                SortOrder: block.SortOrder,
                Version: block.Version,
                Data: null))
            .ToList();

        return new ContentResponse(normalizedSlug, dataBlocks);
    }

    public async Task<UpdatePageResponse> UpdatePageAsync(string clientId, UpdatePageRequest request, string updatedBy, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(request.Slug);

        var blocks = await _repository.GetBySlugAsync(clientId, normalizedSlug, cancellationToken: cancellationToken);
        var blocksByPath = blocks.ToDictionary(b => b.BlockPath);

        var updated = 0;
        var unchanged = 0;
        var utcNow = DateTime.UtcNow;

        foreach (var item in request.Blocks)
        {
            var blockPath = SlugNormalizer.NormalizeBlockPath(item.BlockPath);

            if (!blocksByPath.TryGetValue(blockPath, out var block))
                throw new NotFoundException($"Block '{blockPath}' not found for slug '{normalizedSlug}'.");

            if (block.Value.ToJsonString() == item.Value.ToJsonString())
            {
                unchanged++; continue;
            }

            if (item.Version != block.Version)
                throw new ConcurrencyConflictException($"Version conflict on block '{blockPath}' (slug '{normalizedSlug}'). Expected {block.Version}, got {item.Version}.");

            block.UpdateValue(item.Value, updatedBy, utcNow);
            updated++;
        }

        if (updated > 0)
            await _repository.SaveChangesAsync(cancellationToken);

        await _draftService.DeleteDraftAsync(clientId, updatedBy, normalizedSlug, cancellationToken);

        return new UpdatePageResponse(updated, unchanged);
    }

    public async Task<SyncResultResponse> SyncAsync(string clientId, IReadOnlyList<SyncManifestRequest> manifests, string syncedBy, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        var desiredByKey = new Dictionary<(string Slug, string BlockPath), ManifestBlockItem>();
        var requestSlugs = new HashSet<string>();

        foreach (var manifest in manifests)
        {
            var slug = SlugNormalizer.NormalizeSlug(manifest.Slug);
            requestSlugs.Add(slug);

            foreach (var item in manifest.Blocks)
            {
                var blockPath = SlugNormalizer.NormalizeBlockPath(item.BlockPath);
                desiredByKey[(slug, blockPath)] = item;
            }
        }

        var existing = await _repository.GetByClientAsync(clientId, includeArchived: true, cancellationToken: cancellationToken);
        var existingByKey = existing.ToDictionary(b => (b.Slug, b.BlockPath));

        var counts = requestSlugs.ToDictionary(slug => slug, _ => new SlugCounts());
        var prunedSlugs = new HashSet<string>();

        var toCreate = new List<ContentBlock>();

        foreach (var block in existing)
        {
            var key = (block.Slug, block.BlockPath);

            if (desiredByKey.TryGetValue(key, out var item))
            {
                if (block.IsArchived)
                {
                    block.Restore(syncedBy, utcNow);
                    block.UpdateDefinition(item.BlockType, item.SortOrder, syncedBy, utcNow);
                    counts[block.Slug].Restored++;
                }
                else
                {
                    block.UpdateDefinition(item.BlockType, item.SortOrder, syncedBy, utcNow);
                    counts[block.Slug].Unchanged++;
                }
            }
            else if (!block.IsArchived)
            {
                block.Archive(syncedBy, utcNow);

                if (requestSlugs.Contains(block.Slug))
                    counts[block.Slug].Deleted++;
                else
                    prunedSlugs.Add(block.Slug);
            }
        }

        foreach (var ((slug, blockPath), item) in desiredByKey)
        {
            if (existingByKey.ContainsKey((slug, blockPath)))
                continue;

            toCreate.Add(ContentBlock.Create(
                clientId,
                slug,
                blockPath,
                item.BlockType,
                item.DefaultValue,
                item.SortOrder,
                syncedBy,
                utcNow
            ));
            counts[slug].Created++;
        }

        if (toCreate.Count > 0)
            await _repository.AddRangeAsync(toCreate, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        var results = counts
            .Select(kvp => new SyncSlugResult(kvp.Key, kvp.Value.Created, kvp.Value.Deleted, kvp.Value.Unchanged, kvp.Value.Restored))
            .ToList();

        return new SyncResultResponse(results, prunedSlugs.ToList());
    }

    private sealed class SlugCounts
    {
        public int Created;
        public int Deleted;
        public int Unchanged;
        public int Restored;
    }

    public async Task SaveDraftAsync(string clientId, string userId, UpdatePageRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(request.Slug);

        var draftBlocks = request.Blocks
            .Select(b => new DraftBlock(SlugNormalizer.NormalizeBlockPath(b.BlockPath), b.Value))
            .ToList();

        await _draftService.SaveDraftAsync(clientId, userId, normalizedSlug, draftBlocks, cancellationToken);
    }
}
