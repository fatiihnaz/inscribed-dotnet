using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inscribed.Application.Contracts.Schemas;

public enum ChoiceKind
{
    Static,
    Collection
}

public sealed class ChoiceKindConverter : JsonStringEnumConverter<ChoiceKind>
{
    public ChoiceKindConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}

public sealed record ChoiceSource(
    [property: JsonConverter(typeof(ChoiceKindConverter))] ChoiceKind Kind,
    IReadOnlyList<string>? Values = null,
    string? Collection = null
);

public sealed record FieldMirror(string Field, string Path);

public sealed record FieldDefinition(
    string Name,
    FieldType Type,
    string Label,
    bool Required = false,
    string? Help = null,
    bool ReadOnly = false,
    bool Computed = false,
    bool Filterable = false,
    bool Sortable = false,
    ChoiceSource? Source = null,
    bool AllowCustom = false,
    FieldMirror? From = null,
    IReadOnlyList<FieldDefinition>? ItemFields = null
);
