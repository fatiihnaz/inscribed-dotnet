using System.Security.Claims;
using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Identity;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Schemas;
using Inscribed.Application.Services.Helpers;

namespace Inscribed.Application.Services.Policies;

public sealed class FileCollectionPolicy : ICollectionPolicy
{
    private readonly FileCollectionDefinition _definition;
    private readonly IReadOnlyList<ICollectionEnricher> _enrichers;
    private readonly IAdministratorPolicy _administrators;

    public FileCollectionPolicy(
        FileCollectionDefinition definition,
        IReadOnlyList<ICollectionEnricher> enrichers,
        IAdministratorPolicy administrators)
    {
        _definition = definition;
        _enrichers = enrichers;
        _administrators = administrators;
    }

    public string Key => _definition.Key;

    public CollectionSchema Schema => _definition.Schema;

    public SlugSource SlugSource => _definition.SlugSource;

    public bool AllowAnonymousRead => _definition.AllowAnonymousRead;

    public IReadOnlyList<string> Locales => _definition.Locales;

    public string Source => _definition.Source;

    public bool AppliesTo(string? tenant)
        => _definition.Clients.Count == 0
            || (tenant is not null && _definition.Clients.Contains(tenant, StringComparer.Ordinal));

    public bool CanRead(ClaimsPrincipal user)
        => AccessRuleEvaluator.Allows(_definition.Access?.Read, user);

    public bool CanEdit(ClaimsPrincipal user, string slug)
        => OwnsOrAdministers(user, slug) && AccessRuleEvaluator.Allows(_definition.Access?.Write, user);

    public bool CanCreate(ClaimsPrincipal user)
        => (_definition.ClaimSlug is null || _administrators.IsAdministrator(user))
            && AccessRuleEvaluator.Allows(_definition.Access?.Create, user);

    private bool OwnsOrAdministers(ClaimsPrincipal user, string slug)
    {
        if (_definition.ClaimSlug is not { } rule)
            return true;

        return _administrators.IsAdministrator(user) || OwnsSlug(rule, user, slug);
    }

    public IReadOnlyCollection<string> GetVirtualSlugs(ClaimsPrincipal user, string? locale)
        => _definition.ClaimSlug is { } rule ? ClaimSlugDeriver.Derive(rule, user, locale) : [];

    public bool OwnsVirtualSlug(ClaimsPrincipal user, string slug)
        => _definition.ClaimSlug is { } rule && OwnsSlug(rule, user, slug);

    public string? GetSlugSourceValue(JsonNode data)
        => _definition.SlugSourceField is { } field && data[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public async Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default)
    {
        if (_enrichers.Count == 0)
            return data;

        var enriched = data.DeepClone();

        foreach (var enricher in _enrichers)
            enriched = await enricher.EnrichAsync(slug, enriched, cancellationToken);

        return enriched;
    }

    private bool OwnsSlug(ClaimSlugRule rule, ClaimsPrincipal user, string slug)
    {
        var derived = ClaimSlugDeriver.Derive(rule, user, locale: null);

        if (derived.Contains(slug))
            return true;

        foreach (var locale in _definition.Locales)
        {
            var suffix = $"-{locale}";

            if (slug.EndsWith(suffix, StringComparison.Ordinal) && derived.Contains(slug[..^suffix.Length]))
                return true;
        }

        return false;
    }
}
