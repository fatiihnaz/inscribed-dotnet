using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Identity;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inscribed.Application.Services.Policies;

public sealed record CollectionLoadReport(
    IReadOnlyList<string> Loaded,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Failed)
{
    public bool Complete => Failed.Count == 0;
}

public sealed class CollectionPolicyRegistry : ICollectionPolicyResolver
{
    private sealed record Snapshot(
        IReadOnlyDictionary<string, ICollectionPolicy> Policies,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Misconfigured);

    private static readonly Snapshot Empty = new(
        new Dictionary<string, ICollectionPolicy>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    private readonly IServiceScopeFactory _scopes;
    private readonly IEnumerable<ICollectionPolicy> _codePolicies;
    private readonly ICollectionEnricherFactory _enrichers;
    private readonly IAdministratorPolicy _administrators;
    private readonly IReadOnlyCollection<string> _credentialNames;
    private readonly ILogger<CollectionPolicyRegistry> _logger;

    private volatile Snapshot _snapshot = Empty;

    public CollectionPolicyRegistry(
        IServiceScopeFactory scopes,
        IEnumerable<ICollectionPolicy> codePolicies,
        ICollectionEnricherFactory enrichers,
        IAdministratorPolicy administrators,
        EnrichmentCredentialNames credentialNames,
        ILogger<CollectionPolicyRegistry> logger)
    {
        _scopes = scopes;
        _codePolicies = codePolicies;
        _enrichers = enrichers;
        _administrators = administrators;
        _credentialNames = credentialNames.Names;
        _logger = logger;
    }

    public IReadOnlyCollection<ICollectionPolicy> All => [.. _snapshot.Policies.Values];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Misconfigured => _snapshot.Misconfigured;

    public ICollectionPolicy Resolve(string key)
    {
        var snapshot = _snapshot;

        if (snapshot.Policies.TryGetValue(key, out var policy))
            return policy;

        if (snapshot.Misconfigured.TryGetValue(key, out var errors))
            throw new MisconfiguredCollectionException(key, errors);

        throw new NotFoundException($"Unknown collection '{key}'.");
    }

    public async Task<CollectionLoadReport> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICollectionDefinitionRepository>();
        var rows = await repository.ListAsync(cancellationToken);

        var policies = new Dictionary<string, ICollectionPolicy>(StringComparer.OrdinalIgnoreCase);
        var misconfigured = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var policy in _codePolicies)
        {
            if (policies.TryGetValue(policy.Key, out var existing))
                throw new InvalidOperationException($"Collection key '{policy.Key}' is defined by both {Describe(existing)} and {Describe(policy)}.");

            policies.Add(policy.Key, policy);
        }

        foreach (var row in rows.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            var result = CollectionDefinitionParser.Parse(row.Document, $"db:{row.Key}", _credentialNames);

            if (result.Definition is not { } definition)
            {
                misconfigured[row.Key] = result.Errors;
                continue;
            }

            if (!string.Equals(definition.Key, row.Key, StringComparison.OrdinalIgnoreCase))
            {
                misconfigured[row.Key] = [$"stored key '{row.Key}' does not match the key '{definition.Key}' inside the document"];
                continue;
            }

            if (policies.TryGetValue(definition.Key, out var clash))
            {
                misconfigured[row.Key] = [$"collection key '{definition.Key}' is already defined by {Describe(clash)}"];
                continue;
            }

            policies.Add(definition.Key, Build(definition));
        }

        _snapshot = new Snapshot(policies, misconfigured);

        foreach (var (key, errors) in misconfigured)
            _logger.LogError("Collection '{Key}' is not being served; its definition is invalid:{NewLine}{Errors}",
                key, Environment.NewLine, string.Join(Environment.NewLine, errors.Select(error => $"  - {error}")));

        return new CollectionLoadReport([.. policies.Keys.Order(StringComparer.Ordinal)], misconfigured);
    }

    private ICollectionPolicy Build(FileCollectionDefinition definition) =>
        new FileCollectionPolicy(
            definition,
            definition.Enrichments.Count == 0 ? [] : [.. definition.Enrichments.Select(_enrichers.Create)],
            _administrators);

    private static string Describe(ICollectionPolicy policy)
        => policy is FileCollectionPolicy file ? $"'{file.Source}'" : policy.GetType().Name;
}
