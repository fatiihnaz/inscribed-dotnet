using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Policies;

public sealed class CollectionDefinitionDocument
{
    public string? Key { get; init; }
    public bool AllowAnonymousRead { get; init; }
    public SlugDefinitionDocument? Slug { get; init; }
    public List<FieldDefinitionDocument>? Fields { get; init; }
}

public sealed class SlugDefinitionDocument
{
    public SlugSource? Source { get; init; }
    public string? From { get; init; }
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
    public List<string>? Options { get; init; }
    public List<FieldDefinitionDocument>? ItemFields { get; init; }
}