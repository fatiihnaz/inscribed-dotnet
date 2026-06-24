using System.Security.Claims;
using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Contracts.Responses;
using Inscribed.Application.Contracts.Schemas;
using Inscribed.Application.Contracts.Services;
using Inscribed.Application.Services.Helpers;
using Inscribed.Application.Services.Policies;
using Inscribed.Domain.Entities;
using Inscribed.Domain.Enums;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Application.Services;

public sealed class CollectionService : ICollectionService
{
    private readonly ICollectionItemRepository _repository;
    private readonly ICollectionPolicyResolver _policyResolver;
    private readonly ICollectionDraftService _drafts;

    public CollectionService(
        ICollectionItemRepository repository,
        ICollectionPolicyResolver policyResolver,
        ICollectionDraftService drafts)
    {
        _repository = repository;
        _policyResolver = policyResolver;
        _drafts = drafts;
    }

    public CollectionSchema GetSchema(CollectionKey key)
        => _policyResolver.Resolve(key).Schema;

    public bool AllowsAnonymousRead(CollectionKey key)
        => _policyResolver.Resolve(key).AllowAnonymousRead;

    public IReadOnlyList<MyCollectionResponse> GetMyCollections(ClaimsPrincipal user)
    {
        var result = new List<MyCollectionResponse>();
        foreach (var policy in _policyResolver.All)
        {
            var canCreate = policy.CanCreate(user);
            var hasEditableVirtual = policy.GetVirtualSlugs(user).Count > 0;

            if (!canCreate && !hasEditableVirtual)
                continue;

            result.Add(new MyCollectionResponse(
                CollectionKey: policy.Key.ToString(),
                Schema: policy.Schema,
                CanCreate: canCreate,
                SlugSource: policy.SlugSource.ToString()
            ));
        }
        return result;
    }

    public async Task<PagedListResponse<CollectionItemResponse>> ListAsync(
        CollectionKey key,
        ClaimsPrincipal user,
        string userId,
        IDictionary<string, string>? filters,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        var isAnonymous = string.IsNullOrWhiteSpace(userId);

        var filterJson = filters is { Count: > 0 }
            ? CollectionFilterParser.Build(policy.Schema, filters)
            : null;

        var (items, total) = await _repository.ListPagedAsync(key, filterJson, offset, limit, cancellationToken);

        var responses = new List<CollectionItemResponse>(items.Count);
        var existingSlugs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);

            if (isAnonymous)
            {
                responses.Add(ToResponse(item, enriched, canEdit: null));
                continue;
            }

            var draft = await _drafts.GetItemDraftAsync(key, item.Slug, userId, cancellationToken);
            var draftData = ResolveItemDraft(item.Data, draft?.Data);
            responses.Add(ToResponse(item, enriched, policy.CanEdit(user, item.Slug), draftData));
            existingSlugs.Add(item.Slug);
        }

        if (!isAnonymous && filterJson is null && offset == 0)
        {
            foreach (var virtualSlug in policy.GetVirtualSlugs(user))
            {
                if (existingSlugs.Contains(virtualSlug)) continue;

                var empty = new JsonObject();
                var enriched = await policy.EnrichAsync(virtualSlug, empty, cancellationToken);
                var draft = await _drafts.GetItemDraftAsync(key, virtualSlug, userId, cancellationToken);
                responses.Add(new CollectionItemResponse(
                    Id: Guid.Empty,
                    CollectionKey: key.ToString(),
                    Slug: virtualSlug,
                    Data: enriched,
                    Version: 0,
                    CanEdit: true,
                    DraftData: ResolveNewDraft(draft?.Data)
                ));
            }

            var newDraft = await _drafts.GetNewDraftAsync(key, userId, cancellationToken);
            var newDraftData = ResolveNewDraft(newDraft?.Data);
            if (newDraftData is not null)
            {
                responses.Add(new CollectionItemResponse(
                    Id: Guid.Empty,
                    CollectionKey: key.ToString(),
                    Slug: newDraft!.Slug,
                    Data: new JsonObject(),
                    Version: 0,
                    CanEdit: true,
                    DraftData: newDraftData
                ));
            }
        }

        return new PagedListResponse<CollectionItemResponse>(responses, total, offset, limit);
    }

    public async Task<CollectionItemResponse?> GetAsync(CollectionKey key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);

        var item = await _repository.GetBySlugAsync(key, normalizedSlug, cancellationToken: cancellationToken);
        if (item is null) return null;

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
            return ToResponse(item, enriched, canEdit: null);

        var draft = await _drafts.GetItemDraftAsync(key, item.Slug, userId, cancellationToken);
        return ToResponse(item, enriched, policy.CanEdit(user, item.Slug), ResolveItemDraft(item.Data, draft?.Data));
    }

    public async Task<CollectionItemResponse> UpsertAsync(CollectionKey key, string slug, UpsertCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);

        if (!policy.CanEdit(user, normalizedSlug))
            throw new UnauthorizedAccessException($"User cannot edit '{key}/{normalizedSlug}'.");

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data);

        var utcNow = DateTime.UtcNow;
        var item = await _repository.GetBySlugAsync(key, normalizedSlug, cancellationToken: cancellationToken);

        if (item is null)
        {
            if (policy.SlugSource == SlugSource.AutoGenerated)
                throw new ValidationException([$"Collection '{key}' uses auto-generated slugs; use POST to create items."]);

            if (!policy.CanCreate(user) && !policy.GetVirtualSlugs(user).Contains(normalizedSlug))
                throw new UnauthorizedAccessException($"User cannot create new items in '{key}'.");

            item = CollectionItem.Create(key, normalizedSlug, validated, updatedBy, utcNow);
            await _repository.AddAsync(item, cancellationToken);
        }
        else
        {
            if (request.Version is { } v && v != item.Version)
                throw new ConcurrencyConflictException($"Version conflict on '{key}/{normalizedSlug}'. Expected {item.Version}, got {v}.");

            item.UpdateData(validated, updatedBy, utcNow);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        await _drafts.DeleteItemDraftAsync(key, normalizedSlug, updatedBy, cancellationToken);

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        return ToResponse(item, enriched, canEdit: true);
    }

    public async Task<CollectionItemResponse> CreateAutoSlugAsync(CollectionKey key, CreateCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);

        if (policy.SlugSource != SlugSource.AutoGenerated)
            throw new ValidationException([$"Collection '{key}' does not use auto-generated slugs; use PUT with a slug."]);

        if (!policy.CanCreate(user))
            throw new UnauthorizedAccessException($"User cannot create new items in '{key}'.");

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data);

        var source = policy.GetSlugSourceValue(validated);
        if (string.IsNullOrWhiteSpace(source))
            throw new ValidationException(["Slug source field is missing or empty."]);

        var baseSlug = SlugGenerator.Slugify(source);
        if (string.IsNullOrWhiteSpace(baseSlug))
            throw new ValidationException(["Slug source produced an empty slug."]);

        var slug = await ResolveUniqueSlugAsync(key, baseSlug, cancellationToken);

        var utcNow = DateTime.UtcNow;
        var item = CollectionItem.Create(key, slug, validated, updatedBy, utcNow);
        await _repository.AddAsync(item, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _drafts.DeleteNewDraftAsync(key, updatedBy, cancellationToken);

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        return ToResponse(item, enriched, canEdit: true);
    }

    public async Task SaveItemDraftAsync(CollectionKey key, string slug, string userId, ClaimsPrincipal user, SaveDraftRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);

        if (!policy.CanEdit(user, normalizedSlug))
            throw new UnauthorizedAccessException($"User cannot edit '{key}/{normalizedSlug}'.");

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data, isDraft: true);
        await _drafts.SaveItemDraftAsync(key, normalizedSlug, userId, validated, cancellationToken);
    }

    public async Task SaveNewDraftAsync(CollectionKey key, string userId, ClaimsPrincipal user, SaveNewDraftRequest request, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);

        if (!policy.CanCreate(user) && policy.GetVirtualSlugs(user).Count == 0)
            throw new UnauthorizedAccessException($"User cannot create new items in '{key}'.");

        string? slug = null;
        if (request.Slug is not null)
            slug = SlugNormalizer.NormalizeBlockPath(request.Slug);

        switch (policy.SlugSource)
        {
            case SlugSource.RoleDerived:
                if (slug is null)
                    throw new ValidationException(["Slug is required for role-derived collections."]);
                if (!policy.GetVirtualSlugs(user).Contains(slug))
                    throw new UnauthorizedAccessException($"User cannot create item for slug '{slug}'.");
                if (await _repository.GetBySlugAsync(key, slug, includeArchived: true, cancellationToken) is not null)
                    throw new ValidationException([$"Slug '{slug}' already exists; use item draft endpoint instead."]);
                break;

            case SlugSource.UserDefined:
                if (slug is null)
                    throw new ValidationException(["Slug is required for user-defined collections."]);
                if (await _repository.GetBySlugAsync(key, slug, includeArchived: true, cancellationToken) is not null)
                    throw new ValidationException([$"Slug '{slug}' already exists; use item draft endpoint instead."]);
                break;

            case SlugSource.AutoGenerated:
                slug = null;
                break;
        }

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data, isDraft: true);
        await _drafts.SaveNewDraftAsync(key, userId, slug, validated, cancellationToken);
    }

    private static JsonNode? ResolveItemDraft(JsonNode published, JsonNode? draft)
    {
        if (draft is null) return null;
        var p = JsonNode.Parse(published.ToJsonString());
        var d = JsonNode.Parse(draft.ToJsonString());
        return JsonNode.DeepEquals(d, p) ? null : draft;
    }

    private static JsonNode? ResolveNewDraft(JsonNode? draft)
    {
        if (draft is not JsonObject obj || obj.Count == 0) return null;
        return IsEffectivelyEmpty(obj) ? null : draft;
    }

    private static bool IsEffectivelyEmpty(JsonNode? node) => node switch
    {
        null => true,
        JsonObject obj => obj.All(p => IsEffectivelyEmpty(p.Value)),
        JsonArray arr => arr.Count == 0,
        JsonValue val when val.TryGetValue<string>(out var s) => string.IsNullOrEmpty(s),
        JsonValue val when val.TryGetValue<bool>(out var b) => !b,
        JsonValue val when val.TryGetValue<double>(out var d) => d == 0,
        _ => false,
    };

    private async Task<string> ResolveUniqueSlugAsync(CollectionKey key, string baseSlug, CancellationToken cancellationToken)
    {
        var candidate = baseSlug;
        var n = 2;
        while (await _repository.GetBySlugAsync(key, candidate, includeArchived: true, cancellationToken: cancellationToken) is not null)
        {
            candidate = $"{baseSlug}-{n++}";
        }
        return candidate;
    }

    private static CollectionItemResponse ToResponse(CollectionItem item, JsonNode data, bool? canEdit, JsonNode? draftData = null) =>
        new(
            Id: item.Id,
            CollectionKey: item.CollectionKey.ToString(),
            Slug: item.Slug,
            Data: data,
            Version: item.Version,
            CanEdit: canEdit,
            DraftData: draftData
        );
}