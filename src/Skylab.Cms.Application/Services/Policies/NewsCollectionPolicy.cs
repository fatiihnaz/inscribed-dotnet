using System.Security.Claims;
using System.Text.Json.Nodes;
using Skylab.Cms.Application.Contracts.Policies;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Services.Policies;

public sealed class NewsCollectionPolicy : ICollectionPolicy
{
    public CollectionKey Key => CollectionKey.News;

    public bool CanEdit(ClaimsPrincipal user, string slug) => true;

    public Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default)
        => Task.FromResult(data);
}