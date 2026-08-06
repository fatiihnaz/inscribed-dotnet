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

    public CollectionSchema GetSchema(string key)
        => _policyResolver.Resolve(key).Schema;

    public bool AllowsAnonymousRead(string key)
        => _policyResolver.Resolve(key).AllowAnonymousRead;

    public IReadOnlyList<MyCollectionResponse> GetMyCollections(ClaimsPrincipal user)
    {
        var result = new List<MyCollectionResponse>();
        foreach (var policy in _policyResolver.All)
        {
            var canCreate = policy.CanCreate(user);

            if (!canCreate)
                continue;

            result.Add(new MyCollectionResponse(
                CollectionKey: policy.Key,
                Schema: policy.Schema,
                CanCreate: canCreate,
                SlugSource: policy.SlugSource.ToString(),
                Locales: policy.Locales
            ));
        }
        return result;
    }

    public async Task<PagedListResponse<CollectionItemResponse>> ListAsync(
        string key,
        string? requestedLocale,
        ClaimsPrincipal user,
        string userId,
        IDictionary<string, string>? filters,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;
        var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: false);
        var publishedOnly = string.IsNullOrWhiteSpace(userId);

        var filterJson = filters is { Count: > 0 }
            ? CollectionFilterParser.Build(policy.Schema, filters)
            : null;

        var (items, total) = await _repository.ListPagedAsync(key, locale, filterJson, offset, limit, cancellationToken);

        var responses = new List<CollectionItemResponse>(items.Count);

        foreach (var item in items)
        {
            var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);

            if (publishedOnly)
            {
                responses.Add(ToResponse(item, enriched, canEdit: null));
                continue;
            }

            var draft = await _drafts.GetItemDraftAsync(key, item.Slug, userId, cancellationToken);
            var draftData = ResolveItemDraft(item.Data, draft?.Data);
            responses.Add(ToResponse(item, enriched, policy.CanEdit(user, item.Slug), draftData));
        }

        if (!publishedOnly && filterJson is null && offset == 0)
        {
            var newDraft = await _drafts.GetNewDraftAsync(key, locale, userId, cancellationToken);
            var newDraftData = ResolveNewDraft(newDraft?.Data);
            if (newDraftData is not null)
            {
                responses.Add(new CollectionItemResponse(
                    Id: Guid.Empty,
                    CollectionKey: key,
                    Slug: newDraft!.Slug,
                    Data: new JsonObject(),
                    Version: 0,
                    TranslationGroupId: newDraft.TranslationGroupId ?? Guid.Empty,
                    Locale: locale,
                    CanEdit: true,
                    DraftData: newDraftData
                ));
            }
        }

        return new PagedListResponse<CollectionItemResponse>(responses, total, offset, limit);
    }

    public async Task<CollectionItemResponse?> GetAsync(string key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;

        var item = await _repository.GetBySlugAsync(key, normalizedSlug, cancellationToken: cancellationToken);
        if (item is null) return null;

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        var translations = await LoadTranslationsAsync(key, item, policy.Locales, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
            return ToResponse(item, enriched, canEdit: null, translations: translations);

        var draft = await _drafts.GetItemDraftAsync(key, item.Slug, userId, cancellationToken);
        return ToResponse(item, enriched, policy.CanEdit(user, item.Slug), ResolveItemDraft(item.Data, draft?.Data), translations);
    }

    public async Task<CollectionItemResponse> UpsertAsync(string key, string slug, string? requestedLocale, Guid? translationGroup, UpsertCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;

        if (!policy.CanEdit(user, normalizedSlug))
            throw new UnauthorizedAccessException($"User cannot edit '{key}/{normalizedSlug}'.");

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data);

        var utcNow = DateTime.UtcNow;
        var item = await _repository.GetBySlugAsync(key, normalizedSlug, cancellationToken: cancellationToken);

        if (item is null)
        {
            if (policy.SlugSource == SlugSource.AutoGenerated)
                throw new ValidationException([$"Collection '{key}' uses auto-generated slugs; use POST to create items."]);

            if (!policy.CanCreate(user))
                throw new UnauthorizedAccessException($"User cannot create new items in '{key}'.");

            var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: true);
            var group = await ResolveTranslationGroupAsync(key, translationGroup, locale, cancellationToken);
            item = CollectionItem.Create(key, locale, normalizedSlug, validated, updatedBy, utcNow, group);
            await _repository.AddAsync(item, cancellationToken);
        }
        else
        {
            if (request.Version is not { } v)
                throw new ValidationException([$"Version is required when updating existing item '{key}/{normalizedSlug}'."]);

            if (v != item.Version)
                throw new ConcurrencyConflictException(
                    $"Version conflict on '{key}/{normalizedSlug}'. Expected {item.Version}, got {v}.",
                    [new VersionConflict($"{key}/{normalizedSlug}", item.Version, v)]);

            item.UpdateData(validated, updatedBy, utcNow);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        await _drafts.DeleteItemDraftAsync(key, normalizedSlug, updatedBy, cancellationToken);

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        return ToResponse(item, enriched, canEdit: true);
    }

    public async Task<CollectionItemResponse> CreateAutoSlugAsync(string key, string? requestedLocale, Guid? translationGroup, CreateCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;
        var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: true);

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
        var group = await ResolveTranslationGroupAsync(key, translationGroup, locale, cancellationToken);

        var utcNow = DateTime.UtcNow;
        var item = CollectionItem.Create(key, locale, slug, validated, updatedBy, utcNow, group);
        await _repository.AddAsync(item, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        await _drafts.DeleteNewDraftAsync(key, locale, updatedBy, cancellationToken);

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        var translations = await LoadTranslationsAsync(key, item, policy.Locales, cancellationToken);
        return ToResponse(item, enriched, canEdit: true, translations: translations);
    }

    public async Task SaveItemDraftAsync(string key, string slug, string userId, ClaimsPrincipal user, SaveDraftRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;

        if (!policy.CanEdit(user, normalizedSlug))
            throw new UnauthorizedAccessException($"User cannot edit '{key}/{normalizedSlug}'.");

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data, isDraft: true);
        await _drafts.SaveItemDraftAsync(key, normalizedSlug, userId, validated, cancellationToken);
    }

    public async Task SaveNewDraftAsync(string key, string? requestedLocale, Guid? translationGroup, string userId, ClaimsPrincipal user, SaveNewDraftRequest request, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;
        var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: true);

        if (!policy.CanCreate(user))
            throw new UnauthorizedAccessException($"User cannot create new items in '{key}'.");

        if (translationGroup is { } pending)
            await RequireTranslationGroupAsync(key, pending, cancellationToken);

        string? slug = null;
        if (request.Slug is not null)
            slug = SlugNormalizer.NormalizeBlockPath(request.Slug);

        switch (policy.SlugSource)
        {
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
        await _drafts.SaveNewDraftAsync(key, locale, userId, slug, translationGroup, validated, cancellationToken);
    }

    public Task DiscardItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        return _drafts.DeleteItemDraftAsync(policy.Key, SlugNormalizer.NormalizeBlockPath(slug), userId, cancellationToken);
    }

    public Task DiscardNewDraftAsync(string key, string? requestedLocale, string userId, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: true);
        return _drafts.DeleteNewDraftAsync(policy.Key, locale, userId, cancellationToken);
    }

    private async Task<IReadOnlyList<CollectionItem>> RequireTranslationGroupAsync(string key, Guid translationGroupId, CancellationToken cancellationToken)
    {
        var siblings = await _repository.GetByTranslationGroupAsync(key, translationGroupId, cancellationToken);

        if (siblings.Count == 0)
            throw new ValidationException([$"Translation group '{translationGroupId}' does not exist in collection '{key}'."]);

        return siblings;
    }

    private async Task<Guid?> ResolveTranslationGroupAsync(string key, Guid? translationGroup, string? locale, CancellationToken cancellationToken)
    {
        if (translationGroup is not { } translationGroupId)
            return null;

        var siblings = await RequireTranslationGroupAsync(key, translationGroupId, cancellationToken);

        if (siblings.Any(sibling => string.Equals(sibling.Locale, locale, StringComparison.Ordinal)))
            throw new ConflictException($"This translation group already has an item in locale '{locale ?? "(none)"}'.");

        return translationGroupId;
    }

    private async Task<IReadOnlyList<TranslationRef>?> LoadTranslationsAsync(string key, CollectionItem item, IReadOnlyList<string> locales, CancellationToken cancellationToken)
    {
        if (locales.Count == 0)
            return null;

        var siblings = await _repository.GetByTranslationGroupAsync(key, item.TranslationGroupId, cancellationToken);

        var translations = siblings
            .Where(sibling => sibling.Id != item.Id)
            .Select(sibling => new TranslationRef(sibling.Locale, sibling.Slug))
            .ToList();

        return translations;
    }

    private static JsonNode? ResolveItemDraft(JsonNode published, JsonNode? draft)
    {
        if (draft is null) return null;
        return JsonNode.DeepEquals(draft, published) ? null : draft;
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

    private async Task<string> ResolveUniqueSlugAsync(string key, string baseSlug, CancellationToken cancellationToken)
    {
        var candidate = baseSlug;
        var n = 2;
        while (await _repository.GetBySlugAsync(key, candidate, includeArchived: true, cancellationToken: cancellationToken) is not null)
        {
            candidate = $"{baseSlug}-{n++}";
        }
        return candidate;
    }

    private static CollectionItemResponse ToResponse(
        CollectionItem item,
        JsonNode data,
        bool? canEdit,
        JsonNode? draftData = null,
        IReadOnlyList<TranslationRef>? translations = null) =>
        new(
            Id: item.Id,
            CollectionKey: item.CollectionKey,
            Slug: item.Slug,
            Data: data,
            Version: item.Version,
            TranslationGroupId: item.TranslationGroupId,
            Locale: item.Locale,
            Translations: translations,
            CanEdit: canEdit,
            DraftData: draftData
        );
}
