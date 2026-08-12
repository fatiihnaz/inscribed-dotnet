using System.Security.Claims;
using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Policies;
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
    private const int EnrichmentParallelism = 8;

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

            if (!canCreate && policy.GetVirtualSlugs(user, locale: null).Count == 0)
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

    public async Task<CollectionListResponse> ListAsync(
        string key,
        string? requestedLocale,
        ClaimsPrincipal user,
        string userId,
        IDictionary<string, string>? filters,
        string? sort,
        bool archived,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;
        var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: false);
        var order = CollectionSortParser.Parse(policy.Schema, sort);
        var publishedOnly = string.IsNullOrWhiteSpace(userId);

        if (archived && publishedOnly)
            throw new UnauthorizedAccessException($"Listing archived items of '{key}' is reserved for editors.");

        var filterJson = filters is { Count: > 0 }
            ? CollectionFilterParser.Build(policy.Schema, filters)
            : null;

        var (items, total) = await _repository.ListPagedAsync(key, locale, filterJson, order, archived, offset, limit, cancellationToken);

        var enriched = await EnrichAllAsync(policy, items, cancellationToken);

        var drafts = publishedOnly
            ? []
            : await Task.WhenAll(items.Select(item => _drafts.GetItemDraftAsync(key, item.Slug, userId, cancellationToken)));

        var responses = new List<CollectionItemResponse>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            if (publishedOnly)
            {
                responses.Add(ToResponse(item, enriched[index], canEdit: null));
                continue;
            }

            var draftData = ResolveItemDraft(item.Data, drafts[index]?.Data);
            responses.Add(ToResponse(item, enriched[index], policy.CanEdit(user, item.Slug), draftData));
        }

        var virtualItems = publishedOnly || archived
            ? null
            : await BuildVirtualItemsAsync(policy, user, locale, userId, cancellationToken);

        return new CollectionListResponse(responses, total, offset, limit, virtualItems);
    }

    public async Task<CollectionItemResponse?> GetAsync(string key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;

        var isEditor = !string.IsNullOrWhiteSpace(userId);
        var item = await _repository.GetBySlugAsync(key, normalizedSlug, includeArchived: isEditor, cancellationToken);
        if (item is null) return null;

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        var translations = await LoadTranslationsAsync(key, item, policy.Locales, cancellationToken);

        if (!isEditor)
            return ToResponse(item, enriched, canEdit: null, translations: translations);

        var draft = await _drafts.GetItemDraftAsync(key, item.Slug, userId, cancellationToken);
        return ToResponse(item, enriched, policy.CanEdit(user, item.Slug), ResolveItemDraft(item.Data, draft?.Data), translations);
    }

    public async Task<VirtualItemResponse?> GetVirtualAsync(string key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);

        if (!policy.OwnsVirtualSlug(user, normalizedSlug))
            return null;

        var data = await policy.EnrichAsync(normalizedSlug, new JsonObject(), cancellationToken);
        var draft = await _drafts.GetItemDraftAsync(policy.Key, normalizedSlug, userId, cancellationToken);

        return new VirtualItemResponse(
            CollectionKey: policy.Key,
            Origin: VirtualItemOrigin.Derived,
            Data: data,
            CanEdit: true,
            Slug: normalizedSlug,
            DraftData: draft?.Data is { } pending && !IsEffectivelyEmpty(pending) ? pending : null,
            Locale: policy.Locales.FirstOrDefault(locale => normalizedSlug.EndsWith($"-{locale}", StringComparison.Ordinal)));
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
        var item = await FindWritableAsync(key, normalizedSlug, required: false, cancellationToken);
        string? createdLocale = null;

        if (item is null)
        {
            if (policy.SlugSource == SlugSource.AutoGenerated)
                throw new ValidationException([$"Collection '{key}' uses auto-generated slugs; use POST to create items."]);

            var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: true);

            if (!policy.CanCreate(user) && !policy.GetVirtualSlugs(user, locale).Contains(normalizedSlug))
                throw new UnauthorizedAccessException($"User cannot create new items in '{key}'.");

            var group = await ResolveTranslationGroupAsync(key, translationGroup, locale, cancellationToken)
                ?? await ResolveDerivedTranslationGroupAsync(policy, normalizedSlug, locale, cancellationToken);

            item = CollectionItem.Create(key, locale, normalizedSlug, validated, updatedBy, utcNow, group);
            await _repository.AddAsync(item, cancellationToken);
            createdLocale = locale;
        }
        else
        {
            RequireVersion(key, normalizedSlug, request.Version, item.Version);
            item.UpdateData(validated, updatedBy, utcNow);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        await _drafts.DeleteItemDraftAsync(key, normalizedSlug, updatedBy, cancellationToken);

        if (createdLocale is not null)
            await _drafts.DeletePendingDraftAsync(key, createdLocale, updatedBy, cancellationToken);

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

        await _drafts.DeletePendingDraftAsync(key, locale, updatedBy, cancellationToken);

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        var translations = await LoadTranslationsAsync(key, item, policy.Locales, cancellationToken);
        return ToResponse(item, enriched, canEdit: true, translations: translations);
    }

    public async Task<ArchiveResponse> ArchiveAsync(string key, string slug, int? version, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = RequireEditable(key, normalizedSlug, user);

        var item = (await FindWritableAsync(policy.Key, normalizedSlug, required: true, cancellationToken))!;

        RequireVersion(policy.Key, normalizedSlug, version, item.Version);

        item.Archive(updatedBy, DateTime.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);
        await _drafts.DeleteItemDraftAsync(policy.Key, normalizedSlug, updatedBy, cancellationToken);

        return new ArchiveResponse(policy.Key, normalizedSlug, item.Version);
    }

    public async Task<CollectionItemResponse> RestoreAsync(string key, string slug, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = RequireEditable(key, normalizedSlug, user);

        var item = await _repository.GetBySlugAsync(policy.Key, normalizedSlug, includeArchived: true, cancellationToken)
            ?? throw new NotFoundException($"Item '{policy.Key}/{normalizedSlug}' was not found.");

        item.Restore(updatedBy, DateTime.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);

        var enriched = await policy.EnrichAsync(item.Slug, item.Data, cancellationToken);
        var translations = await LoadTranslationsAsync(policy.Key, item, policy.Locales, cancellationToken);
        return ToResponse(item, enriched, canEdit: true, translations: translations);
    }

    public async Task SaveItemDraftAsync(string key, string slug, string userId, ClaimsPrincipal user, SaveDraftRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedSlug = SlugNormalizer.NormalizeBlockPath(slug);
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;

        if (!policy.CanEdit(user, normalizedSlug))
            throw new UnauthorizedAccessException($"User cannot edit '{key}/{normalizedSlug}'.");

        await FindWritableAsync(key, normalizedSlug, required: false, cancellationToken);

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data, isDraft: true);
        await _drafts.SaveItemDraftAsync(key, normalizedSlug, userId, validated, cancellationToken);
    }

    public async Task SavePendingDraftAsync(string key, string? requestedLocale, Guid? translationGroup, string userId, ClaimsPrincipal user, SavePendingDraftRequest request, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        key = policy.Key;
        var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: true);

        if (policy.SlugSource == SlugSource.ClaimDerived)
            throw new ValidationException(
                [$"Collection '{key}' derives slugs from your claims, so a new item already has an address; use PUT /cms/collections/{key}/{{slug}}/draft instead."]);

        if (!policy.CanCreate(user))
            throw new UnauthorizedAccessException($"User cannot create new items in '{key}'.");

        if (translationGroup is { } pending)
            await RequireTranslationGroupAsync(key, pending, cancellationToken);

        var validated = CollectionSchemaValidator.ValidateAndStrip(policy.Schema, request.Data, isDraft: true);

        await _drafts.SavePendingDraftAsync(
            key,
            locale,
            userId,
            new PendingCollectionDraft(validated, DateTime.UtcNow, translationGroup),
            cancellationToken);
    }

    public Task DiscardItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        return _drafts.DeleteItemDraftAsync(policy.Key, SlugNormalizer.NormalizeBlockPath(slug), userId, cancellationToken);
    }

    public Task DiscardPendingDraftAsync(string key, string? requestedLocale, string userId, CancellationToken cancellationToken = default)
    {
        var policy = _policyResolver.Resolve(key);
        var locale = LocaleResolver.Resolve(policy.Locales, requestedLocale, forWrite: true);
        return _drafts.DeletePendingDraftAsync(policy.Key, locale, userId, cancellationToken);
    }

    private ICollectionPolicy RequireEditable(string key, string slug, ClaimsPrincipal user)
    {
        var policy = _policyResolver.Resolve(key);

        if (!policy.CanEdit(user, slug))
            throw new UnauthorizedAccessException($"User cannot edit '{policy.Key}/{slug}'.");

        return policy;
    }

    private async Task<CollectionItem?> FindWritableAsync(string key, string slug, bool required, CancellationToken cancellationToken)
    {
        var item = await _repository.GetBySlugAsync(key, slug, includeArchived: true, cancellationToken);

        if (item is null)
            return required ? throw new NotFoundException($"Item '{key}/{slug}' was not found.") : null;

        if (item.IsArchived)
            throw new ArchivedException($"{key}/{slug}", item.Version);

        return item;
    }

    private async Task<JsonNode[]> EnrichAllAsync(ICollectionPolicy policy, IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken)
    {
        var enriched = new JsonNode[items.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = EnrichmentParallelism, CancellationToken = cancellationToken },
            async (index, token) => enriched[index] = await policy.EnrichAsync(items[index].Slug, items[index].Data, token));

        return enriched;
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

    private async Task<Guid?> ResolveDerivedTranslationGroupAsync(
        ICollectionPolicy policy,
        string slug,
        string? locale,
        CancellationToken cancellationToken)
    {
        if (policy.SlugSource != SlugSource.ClaimDerived || locale is null || policy.Locales.Count < 2)
            return null;

        var suffix = $"-{locale}";
        if (!slug.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        var baseSlug = slug[..^suffix.Length];
        var siblings = policy.Locales
            .Where(other => !string.Equals(other, locale, StringComparison.Ordinal))
            .Select(other => $"{baseSlug}-{other}")
            .ToList();

        var taken = await _repository.GetTakenSlugsAsync(policy.Key, siblings, cancellationToken);

        return taken
            .OrderBy(entry => entry.Slug, StringComparer.Ordinal)
            .Select(entry => (Guid?)entry.TranslationGroupId)
            .FirstOrDefault();
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

    private static void RequireVersion(string key, string slug, int? version, int current)
    {
        if (version is not { } provided)
            throw new ValidationException([$"Version is required when writing to existing item '{key}/{slug}'."]);

        if (provided != current)
            throw new ConcurrencyConflictException(
                $"Version conflict on '{key}/{slug}'. Expected {current}, got {provided}.",
                [new VersionConflict($"{key}/{slug}", current, provided)]);
    }

    private static JsonNode? ResolveItemDraft(JsonNode published, JsonNode? draft)
    {
        if (draft is null) return null;
        return JsonNode.DeepEquals(draft, published) ? null : draft;
    }

    private async Task<IReadOnlyList<VirtualItemResponse>?> BuildVirtualItemsAsync(
        ICollectionPolicy policy,
        ClaimsPrincipal user,
        string? locale,
        string userId,
        CancellationToken cancellationToken)
    {
        var virtualItems = new List<VirtualItemResponse>();

        var draft = await _drafts.GetPendingDraftAsync(policy.Key, locale, userId, cancellationToken);

        if (ToPendingItem(policy.Key, draft, locale) is { } pending)
            virtualItems.Add(pending);

        var derived = policy.GetVirtualSlugs(user, locale);

        if (derived.Count == 0)
            return virtualItems.Count == 0 ? null : virtualItems;

        var rows = (await _repository.GetBySlugsAsync(policy.Key, derived, cancellationToken))
            .ToDictionary(row => row.Slug, StringComparer.Ordinal);

        var offered = derived
            .Where(slug => rows.GetValueOrDefault(slug) is null or { IsArchived: true })
            .Order(StringComparer.Ordinal)
            .ToList();

        var enriched = new JsonNode[offered.Count];
        var drafts = new JsonObject?[offered.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, offered.Count),
            new ParallelOptions { MaxDegreeOfParallelism = EnrichmentParallelism, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var slug = offered[index];
                var row = rows.GetValueOrDefault(slug);

                enriched[index] = await policy.EnrichAsync(slug, row?.Data ?? new JsonObject(), token);

                if (row is null)
                    drafts[index] = (await _drafts.GetItemDraftAsync(policy.Key, slug, userId, token))?.Data;
            });

        for (var index = 0; index < offered.Count; index++)
        {
            var slug = offered[index];

            if (rows.GetValueOrDefault(slug) is { } row)
            {
                virtualItems.Add(new VirtualItemResponse(
                    CollectionKey: policy.Key,
                    Origin: VirtualItemOrigin.Derived,
                    Data: enriched[index],
                    CanEdit: true,
                    Slug: slug,
                    Locale: row.Locale,
                    TranslationGroupId: row.TranslationGroupId,
                    CreatedAt: row.CreatedAt,
                    UpdatedAt: row.UpdatedAt,
                    Id: row.Id,
                    IsArchived: true,
                    Version: row.Version));
                continue;
            }

            virtualItems.Add(new VirtualItemResponse(
                CollectionKey: policy.Key,
                Origin: VirtualItemOrigin.Derived,
                Data: enriched[index],
                CanEdit: true,
                Slug: slug,
                DraftData: drafts[index] is { } pendingData && !IsEffectivelyEmpty(pendingData) ? pendingData : null,
                Locale: locale));
        }

        return virtualItems.Count == 0 ? null : virtualItems;
    }

    private static VirtualItemResponse? ToPendingItem(string key, PendingCollectionDraft? draft, string? locale)
    {
        if (draft?.Data is not { } data || IsEffectivelyEmpty(data))
            return null;

        return new VirtualItemResponse(
            CollectionKey: key,
            Origin: VirtualItemOrigin.Pending,
            Data: new JsonObject(),
            CanEdit: true,
            DraftData: data,
            Locale: locale,
            TranslationGroupId: draft.TranslationGroupId,
            UpdatedAt: draft.UpdatedAt);
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
            CreatedAt: item.CreatedAt,
            UpdatedAt: item.UpdatedAt,
            Locale: item.Locale,
            Translations: translations,
            CanEdit: canEdit,
            DraftData: draftData,
            IsArchived: canEdit is not null && item.IsArchived ? true : null,
            ArchivedAt: canEdit is null ? null : item.ArchivedAt
        );
}
