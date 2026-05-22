using System.Security.Claims;
using System.Text.Json.Nodes;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Contracts.Policies;

public interface ICollectionPolicy
{
    CollectionKey Key { get; }

    bool CanEdit(ClaimsPrincipal user, string slug);

    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}
