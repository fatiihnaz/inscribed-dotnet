namespace Inscribed.Application.Contracts.Policies;

public sealed record EnrichmentDefinition(
    string UrlTemplate,
    string? CredentialName,
    int CacheSeconds,
    IReadOnlyDictionary<string, string> Map
);
