using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Policies;

public sealed class CollectionDefinitionDocument
{
    public string? Key { get; init; }
    public bool AllowAnonymousRead { get; init; }
    public List<string>? Locales { get; init; }
    public SlugDefinitionDocument? Slug { get; init; }
    public List<FieldDefinitionDocument>? Fields { get; init; }
    public List<EnrichmentDocument>? Enrich { get; init; }
}

public sealed class EnrichmentDocument
{
    public string? Url { get; init; }
    public string? Auth { get; init; }
    public int? CacheSeconds { get; init; }
    public Dictionary<string, JsonNode>? Map { get; init; }
}

public sealed class MapTargetDocument
{
    public string? Path { get; init; }
    public FieldType? Type { get; init; }
    public string? Label { get; init; }
}

public sealed class SlugDefinitionDocument
{
    public SlugSource? Source { get; init; }
    public string? From { get; init; }
    public string? Claim { get; init; }
    public string? EndsWith { get; init; }
    public string? StartsWith { get; init; }
    public string? Pattern { get; init; }
}

public sealed class FieldDefinitionDocument
{
    public string? Name { get; init; }
    public FieldType? Type { get; init; }
    public string? Label { get; init; }
    public bool Required { get; init; }
    public string? Help { get; init; }
    public bool ReadOnly { get; init; }
    public bool Filterable { get; init; }
    public bool Sortable { get; init; }
    public List<string>? Options { get; init; }
    public List<FieldDefinitionDocument>? ItemFields { get; init; }
}