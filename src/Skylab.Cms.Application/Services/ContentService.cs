using System.Text.Json.Nodes;
using Skylab.Cms.Application.Contracts.Repositories;
using Skylab.Cms.Application.Contracts.Requests;
using Skylab.Cms.Application.Contracts.Responses;
using Skylab.Cms.Application.Contracts.Services;
using Skylab.Cms.Application.Services.Helpers;
using Skylab.Cms.Domain.Entities;
using Skylab.Cms.Domain.Enums;
using Skylab.Cms.Domain.Exceptions;

namespace Skylab.Cms.Application.Services;

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

    public async Task<SyncResultResponse> SyncAsync(string clientId, SyncManifestRequest request, string syncedBy, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeSlug(request.Slug);
        var utcNow = DateTime.UtcNow;

        var existing = await _repository.GetBySlugAsync(clientId, normalizedSlug, cancellationToken: cancellationToken);
        var existingByPath = existing.ToDictionary(b => b.BlockPath);

        var manifestByPath = request.Blocks.ToDictionary(b => SlugNormalizer.NormalizeBlockPath(b.BlockPath));

        var toCreate = new List<ContentBlock>();
        foreach (var (blockPath, item) in manifestByPath)
        {
            if (existingByPath.ContainsKey(blockPath))
                continue;

            toCreate.Add(ContentBlock.Create(
                clientId,
                normalizedSlug,
                blockPath,
                item.BlockType,
                item.DefaultValue,
                item.SortOrder,
                syncedBy,
                utcNow
            ));
        }

        var toArchive = new List<ContentBlock>();
        var unchanged = 0;

        foreach (var (blockPath, block) in existingByPath)
        {
            if (manifestByPath.TryGetValue(blockPath, out var item))
            {
                var drifted = block.UpdateDefinition(item.BlockType, item.SortOrder, syncedBy, utcNow);
                if (!drifted) unchanged++;
            }
            else
            {
                block.Archive(syncedBy, utcNow);
                toArchive.Add(block);
            }
        }

        if (toCreate.Count > 0)
            await _repository.AddRangeAsync(toCreate, cancellationToken);

        if (toArchive.Count > 0)
            _repository.ArchiveRange(toArchive);

        await _repository.SaveChangesAsync(cancellationToken);

        return new SyncResultResponse(toCreate.Count, toArchive.Count, unchanged);
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
