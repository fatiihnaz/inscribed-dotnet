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

    Task<IReadOnlyList<CollectionItemResponse>> ListAsync(CollectionKey key, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse?> GetAsync(CollectionKey key, string slug, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> UpsertAsync(CollectionKey key, string slug, UpsertCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);

    Task<CollectionItemResponse> CreateAutoSlugAsync(CollectionKey key, CreateCollectionItemRequest request, ClaimsPrincipal user, string updatedBy, CancellationToken cancellationToken = default);
}