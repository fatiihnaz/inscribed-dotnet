using System.Security.Claims;
using Skylab.Cms.Application.Contracts.Requests;
using Skylab.Cms.Application.Contracts.Responses;
using Skylab.Cms.Application.Contracts.Schemas;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Services;

public interface ICollectionService
{
    CollectionSchema GetSchema(CollectionKey key);

    IReadOnlyList<MyCollectionResponse> GetMyCollections(ClaimsPrincipal user);

    Task<PagedListResponse<CollectionItemResponse>> ListAsync(
        CollectionKey key,
        ClaimsPrincipal user,
        string userId,
        IDictionary<string, string>? filters,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CollectionItemResponse?> GetAsync(CollectionKey key, string slug, ClaimsPrincipal user, string userId, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> UpsertAsync(CollectionKey key, string slug, UpsertCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> CreateAutoSlugAsync(CollectionKey key, CreateCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task SaveItemDraftAsync(CollectionKey key, string slug, string userId, ClaimsPrincipal user, SaveDraftRequest request, CancellationToken cancellationToken = default);

    Task SaveNewDraftAsync(CollectionKey key, string userId, ClaimsPrincipal user, SaveNewDraftRequest request, CancellationToken cancellationToken = default);
}
