namespace Inscribed.Application.Contracts.Schemas;

public sealed record FieldDefinition(
    string Name,
    FieldType Type,
    string Label,
    bool Required = false,
    string? Help = null,
    bool ReadOnly = false,
    bool Filterable = false,
    IReadOnlyList<string>? Options = null
);
