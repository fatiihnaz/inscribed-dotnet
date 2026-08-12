using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Contracts.Policies;

public sealed record EnrichmentTarget(
    string Name,
    string Path,
    FieldType Type,
    string Label
);

public sealed record EnrichmentDefinition(
    string UrlTemplate,
    string? CredentialName,
    int CacheSeconds,
    IReadOnlyList<EnrichmentTarget> Targets
);
