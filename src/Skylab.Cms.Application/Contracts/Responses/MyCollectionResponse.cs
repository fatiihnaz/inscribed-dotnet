using Skylab.Cms.Application.Contracts.Schemas;

namespace Skylab.Cms.Application.Contracts.Responses;

public sealed record MyCollectionResponse(
    string CollectionKey,
    CollectionSchema Schema,
    bool CanCreate,
    string SlugSource
);
