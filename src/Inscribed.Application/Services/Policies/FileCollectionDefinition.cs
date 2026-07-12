using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Policies;

public sealed record FileCollectionDefinition(
    string Key,
    CollectionSchema Schema,
    SlugSource SlugSource,
    string? SlugSourceField,
    bool AllowAnonymousRead,
    string SourceFile,
    IReadOnlyList<EnrichmentDefinition> Enrichments
);
