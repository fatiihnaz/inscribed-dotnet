namespace Skylab.Cms.Application.Contracts.Schemas;

public sealed record CollectionSchema(
    IReadOnlyList<FieldDefinition> Fields
);
