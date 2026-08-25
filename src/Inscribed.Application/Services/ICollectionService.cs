using System.Security.Claims;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Contracts.Responses;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services;

public interface ICollectionService
{
    Task<CollectionSchemaResponse> GetSchemaAsync(string key, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<bool> AllowsAnonymousReadAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MyCollectionResponse>> GetMyCollectionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<CollectionListResponse> ListAsync(
        string key,
        string? requestedLocale,
        ClaimsPrincipal user,
        string userId,
        IDictionary<string, string>? filters,
        string? sort,
        bool archived,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CollectionLookupResponse> LookupAsync(
        string key,
        string? query,
        IReadOnlyCollection<string>? slugs,
        string? requestedLocale,
        ClaimsPrincipal user,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CollectionItemResponse?> GetAsync(string key, string slug, string? requestedLocale, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default);

    Task<VirtualItemResponse?> GetVirtualAsync(string key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> UpsertAsync(string key, string slug, string? requestedLocale, Guid? translationGroup, UpsertCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, bool replaceAlias = false, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> CreateAutoSlugAsync(string key, string? requestedLocale, Guid? translationGroup, CreateCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<ArchiveResponse> ArchiveAsync(string key, string slug, int? version, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> RestoreAsync(string key, string slug, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> RenameSlugAsync(string key, string slug, RenameSlugRequest request, ClaimsPrincipal user, string updatedBy, bool replaceAlias, CancellationToken cancellationToken = default);

    Task ReleaseAliasAsync(string key, string slug, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task SaveItemDraftAsync(string key, string slug, string userId, ClaimsPrincipal user, SaveDraftRequest request, CancellationToken cancellationToken = default);

    Task SavePendingDraftAsync(string key, string? requestedLocale, Guid? translationGroup, string userId, ClaimsPrincipal user, SavePendingDraftRequest request, CancellationToken cancellationToken = default);

    Task DiscardItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default);

    Task DiscardPendingDraftAsync(string key, string? requestedLocale, string userId, CancellationToken cancellationToken = default);
}
