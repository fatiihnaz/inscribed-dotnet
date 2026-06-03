namespace Inscribed.Application.Contracts.Schemas;

public sealed record CollectionSchema(
    IReadOnlyList<FieldDefinition> Fields
);
