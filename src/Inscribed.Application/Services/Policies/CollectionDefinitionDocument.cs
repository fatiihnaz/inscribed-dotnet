using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Policies;

public sealed class CollectionDefinitionDocument
{
    public string? Key { get; init; }
    public bool AllowAnonymousRead { get; init; }
    public List<string>? Clients { get; init; }
    public AccessDocument? Access { get; init; }
    public List<string>? Locales { get; init; }
    public SlugDefinitionDocument? Slug { get; init; }
    public List<FieldDefinitionDocument>? Fields { get; init; }
    public List<EnrichmentDocument>? Enrich { get; init; }
}

public sealed class AccessDocument
{
    public AccessRuleDocument? Read { get; init; }
    public AccessRuleDocument? Create { get; init; }
    public AccessRuleDocument? Write { get; init; }
}

public sealed class AccessRuleDocument
{
    public string? Claim { get; init; }
    public List<string>? AnyOf { get; init; }
    public List<string>? AllOf { get; init; }

    [JsonPropertyName("equals")]
    public string? EqualTo { get; init; }

    public bool? Present { get; init; }
    public List<AccessRuleDocument>? All { get; init; }
    public List<AccessRuleDocument>? Any { get; init; }
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
    public string? Type { get; init; }
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
    public bool Editable { get; init; }
}

public sealed class FieldDefinitionDocument
{
    public string? Name { get; init; }
    public string? Type { get; init; }
    public string? Label { get; init; }
    public bool Required { get; init; }
    public string? Help { get; init; }
    public bool ReadOnly { get; init; }
    public bool Filterable { get; init; }
    public bool Sortable { get; init; }
    public ChoiceSourceDocument? Source { get; init; }
    public bool AllowCustom { get; init; }
    public FieldMirrorDocument? From { get; init; }
    public List<string>? Options { get; init; }
    public List<FieldDefinitionDocument>? ItemFields { get; init; }
}

public sealed class ChoiceSourceDocument
{
    public string? Kind { get; init; }
    public List<string>? Values { get; init; }
    public string? Collection { get; init; }
}

public sealed class FieldMirrorDocument
{
    public string? Field { get; init; }
    public string? Path { get; init; }
}