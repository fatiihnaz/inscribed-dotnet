using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Policies;

public sealed record FileCollectionDefinition(
    string Key,
    CollectionSchema Schema,
    SlugSource SlugSource,
    string? SlugSourceField,
    ClaimSlugRule? ClaimSlug,
    bool AllowAnonymousRead,
    IReadOnlyList<string> Clients,
    CollectionAccess? Access,
    IReadOnlyList<string> Locales,
    string Source,
    IReadOnlyList<EnrichmentDefinition> Enrichments
);
