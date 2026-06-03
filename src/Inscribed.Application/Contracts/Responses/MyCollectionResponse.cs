using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Contracts.Responses;

public sealed record MyCollectionResponse(
    string CollectionKey,
    CollectionSchema Schema,
    bool CanCreate,
    string SlugSource
);
