using System.Text.Json.Nodes;

namespace Inscribed.Application.Contracts.Policies;

public interface ICollectionEnricher
{
    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}
