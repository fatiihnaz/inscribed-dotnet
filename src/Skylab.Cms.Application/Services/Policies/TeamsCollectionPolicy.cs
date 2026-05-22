using System.Security.Claims;
using System.Text.Json.Nodes;
using Skylab.Cms.Application.Contracts.Policies;
using Skylab.Cms.Application.Services.Helpers;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Services.Policies;

public sealed class TeamsCollectionPolicy : ICollectionPolicy
{
    public CollectionKey Key => CollectionKey.Teams;

    public bool CanEdit(ClaimsPrincipal user, string slug)
    {
        var leaderSlugs = TeamRoleParser.GetLeaderTeamSlugs(user);
        return leaderSlugs.Contains(slug);
    }

    public Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default)
    {
        // TODO: external API call — fetch leads + memberCount by team slug, merge into data.
        // For now, return stored data unchanged.
        return Task.FromResult(data);
    }
}