using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Contracts.Responses;

public sealed record CollectionSchemaResponse(
    string CollectionKey,
    CollectionSchema Schema,
    string SlugSource,
    bool SlugEditable,
    IReadOnlyList<string> Locales
);
