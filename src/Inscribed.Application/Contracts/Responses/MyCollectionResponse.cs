using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Contracts.Responses;

public sealed record MyCollectionResponse(
    string CollectionKey,
    string DisplayName,
    CollectionSchema Schema,
    bool CanCreate,
    int? ItemCount,
    string SlugSource,
    bool SlugEditable,
    string? DisplayField,
    IReadOnlyList<string> Locales
);
