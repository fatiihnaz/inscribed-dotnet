using System.Text.Json.Nodes;

namespace Inscribed.Application.Contracts.Services;

public sealed record CollectionDraft(string? Slug, JsonObject Data, DateTime UpdatedAt);

public sealed record PendingCollectionDraft(
    JsonObject Data,
    DateTime UpdatedAt,
    Guid? TranslationGroupId = null);

public interface ICollectionDraftService
{
    Task SaveItemDraftAsync(string key, string slug, string userId, JsonObject data, CancellationToken cancellationToken = default);

    Task<CollectionDraft?> GetItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default);

    Task DeleteItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default);

    Task SavePendingDraftAsync(string key, string? locale, string userId, PendingCollectionDraft draft, CancellationToken cancellationToken = default);

    Task<PendingCollectionDraft?> GetPendingDraftAsync(string key, string? locale, string userId, CancellationToken cancellationToken = default);

    Task DeletePendingDraftAsync(string key, string? locale, string userId, CancellationToken cancellationToken = default);
}
