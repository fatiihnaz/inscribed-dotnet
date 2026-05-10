using System.Text.Json.Nodes;

namespace Skylab.Cms.Application.Contracts.Services;

public record DraftBlock(string BlockPath, JsonNode? Value);

public interface IDraftService
{
    Task SaveDraftAsync(string clientId, string slug, IReadOnlyList<DraftBlock> blocks, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftBlock>?> GetDraftAsync(string clientId, string slug, CancellationToken cancellationToken = default);

    Task DeleteDraftAsync(string clientId, string slug, CancellationToken cancellationToken = default);
}