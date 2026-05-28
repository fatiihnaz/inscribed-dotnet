using Skylab.Cms.Application.Contracts.Requests;
using Skylab.Cms.Application.Contracts.Responses;

namespace Skylab.Cms.Application.Services;

public interface IContentService
{
    Task<ContentResponse> GetBySlugAsync(string clientId, string userId, string slug, CancellationToken cancellationToken = default);

    Task<ContentResponse> GetDataBySlugAsync(string clientId, string slug, CancellationToken cancellationToken = default);

    Task<UpdatePageResponse> UpdatePageAsync(string clientId, UpdatePageRequest request, string updatedBy, CancellationToken cancellationToken = default);

    Task<SyncResultResponse> SyncAsync(string clientId, SyncManifestRequest request, string syncedBy, CancellationToken cancellationToken = default);

    Task SaveDraftAsync(string clientId, string userId, UpdatePageRequest request, CancellationToken cancellationToken = default);
}
