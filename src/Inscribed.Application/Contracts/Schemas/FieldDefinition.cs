namespace Inscribed.Application.Contracts.Schemas;

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
    IReadOnlyList<string>? Options = null,
    IReadOnlyList<FieldDefinition>? ItemFields = null
);
