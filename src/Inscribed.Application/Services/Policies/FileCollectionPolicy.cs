using System.Security.Claims;
using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Policies;

public sealed class FileCollectionPolicy : ICollectionPolicy
{
    private readonly FileCollectionDefinition _definition;
    private readonly IReadOnlyList<ICollectionEnricher> _enrichers;

    public FileCollectionPolicy(FileCollectionDefinition definition, IReadOnlyList<ICollectionEnricher> enrichers)
    {
        _definition = definition;
        _enrichers = enrichers;
    }

    public string Key => _definition.Key;

    public CollectionSchema Schema => _definition.Schema;

    public SlugSource SlugSource => _definition.SlugSource;

    public bool AllowAnonymousRead => _definition.AllowAnonymousRead;

    public string SourceFile => _definition.SourceFile;

    public bool CanEdit(ClaimsPrincipal user, string slug) => true;

    public bool CanCreate(ClaimsPrincipal user) => true;

    public string? GetSlugSourceValue(JsonNode data)
        => _definition.SlugSourceField is { } field && data[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public async Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default)
    {
        if (_enrichers.Count == 0)
            return data;

        var enriched = data.DeepClone();

        foreach (var enricher in _enrichers)
            enriched = await enricher.EnrichAsync(slug, enriched, cancellationToken);

        return enriched;
    }
}
