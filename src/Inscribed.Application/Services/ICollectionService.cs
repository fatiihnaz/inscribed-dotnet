using System.Security.Claims;
using Inscribed.Application.Contracts.Requests;
using Inscribed.Application.Contracts.Responses;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services;

public interface ICollectionService
{
    CollectionSchema GetSchema(string key);

    bool AllowsAnonymousRead(string key);

    IReadOnlyList<MyCollectionResponse> GetMyCollections(ClaimsPrincipal user);

    Task<PagedListResponse<CollectionItemResponse>> ListAsync(
        string key,
        ClaimsPrincipal user,
        string userId,
        IDictionary<string, string>? filters,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CollectionItemResponse?> GetAsync(string key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> UpsertAsync(string key, string slug, UpsertCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> CreateAutoSlugAsync(string key, CreateCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task SaveItemDraftAsync(string key, string slug, string userId, ClaimsPrincipal user, SaveDraftRequest request, CancellationToken cancellationToken = default);

    Task SaveNewDraftAsync(string key, string userId, ClaimsPrincipal user, SaveNewDraftRequest request, CancellationToken cancellationToken = default);
}
