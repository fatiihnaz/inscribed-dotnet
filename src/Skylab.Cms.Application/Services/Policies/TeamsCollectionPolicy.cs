using System.Security.Claims;
using System.Text.Json.Nodes;
using Skylab.Cms.Application.Contracts.Policies;
using Skylab.Cms.Application.Contracts.Schemas;
using Skylab.Cms.Application.Services.Helpers;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Services.Policies;

public sealed class TeamsCollectionPolicy : ICollectionPolicy
{
    public CollectionKey Key => CollectionKey.Teams;

    public SlugSource SlugSource => SlugSource.RoleDerived;

    public bool AllowAnonymousRead => true;

    public CollectionSchema Schema { get; } = new([
        new("desc", FieldType.Text, "Açıklama", Required: true),
        new("longDesc", FieldType.RichText, "Uzun Açıklama"),
        new("topics", FieldType.StringArray, "Konular"),
        new("stack", FieldType.StringArray, "Teknolojiler"),
        new("recruiting", FieldType.Bool, "Alım Durumu", Required: true),
        new("recruitingFor", FieldType.Text, "Pozisyon"),
        new("applyUrl", FieldType.Url, "Başvuru Linki"),
        new("leads", FieldType.StringArray, "Liderler", ReadOnly: true),
        new("memberCount", FieldType.Number, "Üye Sayısı", ReadOnly: true),
    ]);

    public bool CanEdit(ClaimsPrincipal user, string slug)
    {
        var leaderSlugs = TeamRoleParser.GetLeaderTeamSlugs(user);
        return leaderSlugs.Contains(slug);
    }

    public bool CanCreate(ClaimsPrincipal user) => false;

    public IReadOnlyCollection<string> GetVirtualSlugs(ClaimsPrincipal user)
        => [.. TeamRoleParser.GetLeaderTeamSlugs(user)];

    public Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default)
    {
        // TODO: external API call — fetch leads + memberCount by team slug, merge into data.
        // For now, return stored data unchanged.
        return Task.FromResult(data);
    }
}
