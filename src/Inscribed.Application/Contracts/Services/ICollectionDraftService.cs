using System.Text.Json.Nodes;
using Inscribed.Domain.Enums;

namespace Inscribed.Application.Contracts.Services;

public sealed record CollectionDraft(string? Slug, JsonObject Data, DateTime UpdatedAt);

public interface ICollectionDraftService
{
    Task SaveItemDraftAsync(CollectionKey key, string slug, string userId, JsonObject data, CancellationToken cancellationToken = default);

    Task<CollectionDraft?> GetItemDraftAsync(CollectionKey key, string slug, string userId, CancellationToken cancellationToken = default);

    Task DeleteItemDraftAsync(CollectionKey key, string slug, string userId, CancellationToken cancellationToken = default);

    Task SaveNewDraftAsync(CollectionKey key, string userId, string? slug, JsonObject data, CancellationToken cancellationToken = default);

    Task<CollectionDraft?> GetNewDraftAsync(CollectionKey key, string userId, CancellationToken cancellationToken = default);

    Task DeleteNewDraftAsync(CollectionKey key, string userId, CancellationToken cancellationToken = default);
}
