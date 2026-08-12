using System.Security.Claims;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Contracts.Responses;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services;

public interface ICollectionService
{
    CollectionSchema GetSchema(string key);

    bool AllowsAnonymousRead(string key);

    IReadOnlyList<MyCollectionResponse> GetMyCollections(ClaimsPrincipal user);

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

    Task<CollectionItemResponse?> GetAsync(string key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default);

    Task<VirtualItemResponse?> GetVirtualAsync(string key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> UpsertAsync(string key, string slug, string? requestedLocale, Guid? translationGroup, UpsertCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> CreateAutoSlugAsync(string key, string? requestedLocale, Guid? translationGroup, CreateCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<ArchiveResponse> ArchiveAsync(string key, string slug, int? version, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> RestoreAsync(string key, string slug, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task SaveItemDraftAsync(string key, string slug, string userId, ClaimsPrincipal user, SaveDraftRequest request, CancellationToken cancellationToken = default);

    Task SavePendingDraftAsync(string key, string? requestedLocale, Guid? translationGroup, string userId, ClaimsPrincipal user, SavePendingDraftRequest request, CancellationToken cancellationToken = default);

    Task DiscardItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default);

    Task DiscardPendingDraftAsync(string key, string? requestedLocale, string userId, CancellationToken cancellationToken = default);
}
