using System.Text.Json.Nodes;

namespace Inscribed.Application.Contracts.Services;

public sealed record CollectionDraft(string? Slug, JsonObject Data, DateTime UpdatedAt, Guid? TranslationGroupId = null);

public interface ICollectionDraftService
{
    Task SaveItemDraftAsync(string key, string slug, string userId, JsonObject data, CancellationToken cancellationToken = default);

    Task<CollectionDraft?> GetItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default);

    Task DeleteItemDraftAsync(string key, string slug, string userId, CancellationToken cancellationToken = default);

    Task SaveNewDraftAsync(string key, string? locale, string userId, string? slug, Guid? translationGroupId, JsonObject data, CancellationToken cancellationToken = default);

    Task<CollectionDraft?> GetNewDraftAsync(string key, string? locale, string userId, CancellationToken cancellationToken = default);

    Task DeleteNewDraftAsync(string key, string? locale, string userId, CancellationToken cancellationToken = default);
}
