using System.Collections.Concurrent;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Identity;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;
using Inscribed.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inscribed.Application.Services.Policies;

public sealed class CollectionPolicyRegistry : ICollectionPolicyResolver
{
    private sealed record Entry(int Version, ICollectionPolicy? Policy, IReadOnlyList<string>? Errors);

    private readonly ConcurrentDictionary<string, Entry> _stored = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ICollectionPolicy> _code = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceScopeFactory _scopes;
    private readonly ICollectionEnricherFactory _enrichers;
    private readonly IAdministratorPolicy _administrators;
    private readonly IReadOnlyCollection<string> _credentialNames;
    private readonly ILogger<CollectionPolicyRegistry> _logger;

    public CollectionPolicyRegistry(
        IServiceScopeFactory scopes,
        IEnumerable<ICollectionPolicy> codePolicies,
        ICollectionEnricherFactory enrichers,
        IAdministratorPolicy administrators,
        EnrichmentCredentialNames credentialNames,
        ILogger<CollectionPolicyRegistry> logger)
    {
        _scopes = scopes;
        _enrichers = enrichers;
        _administrators = administrators;
        _credentialNames = credentialNames.Names;
        _logger = logger;

        foreach (var policy in codePolicies)
        {
            if (_code.TryGetValue(policy.Key, out var existing))
                throw new InvalidOperationException($"Collection key '{policy.Key}' is defined by both {Describe(existing)} and {Describe(policy)}.");

            _code.Add(policy.Key, policy);
        }
    }

    public async Task<ICollectionPolicy> ResolveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_code.TryGetValue(key, out var code))
            return code;

        using var scope = _scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICollectionDefinitionRepository>();

        if (await repository.GetVersionAsync(key, cancellationToken) is not { } version)
        {
            _stored.TryRemove(key, out _);
            throw new NotFoundException($"Unknown collection '{key}'.");
        }

        if (_stored.TryGetValue(key, out var cached) && cached.Version == version)
            return Serve(key, cached);

        if (await repository.GetAsync(key, cancellationToken) is not { } row)
        {
            _stored.TryRemove(key, out _);
            throw new NotFoundException($"Unknown collection '{key}'.");
        }

        var entry = Read(row);
        _stored[row.Key] = entry;

        if (entry.Errors is { } errors)
            LogInvalid(row.Key, errors);

        return Serve(row.Key, entry);
    }

    public async Task<IReadOnlyCollection<ICollectionPolicy>> AllAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICollectionDefinitionRepository>();
        var stamps = await repository.ListStampsAsync(cancellationToken);

        if (!Matches(stamps))
            await RebuildAsync(repository, cancellationToken);

        return
        [
            .. _code.Values,
            .. stamps
                .Select(stamp => _stored.TryGetValue(stamp.Key, out var entry) ? entry.Policy : null)
                .OfType<ICollectionPolicy>()
        ];
    }

    private async Task RebuildAsync(ICollectionDefinitionRepository repository, CancellationToken cancellationToken)
    {
        var rows = await repository.ListAsync(cancellationToken);

        _stored.Clear();

        foreach (var row in rows)
        {
            var entry = Read(row);
            _stored[row.Key] = entry;

            if (entry.Errors is { } errors)
                LogInvalid(row.Key, errors);
        }
    }

    private Entry Read(CollectionDefinition row)
    {
        if (_code.TryGetValue(row.Key, out var clash))
            return new Entry(row.Version, null, [$"collection key '{row.Key}' is already defined by {Describe(clash)}"]);

        var result = CollectionDefinitionParser.Parse(row.Document, $"db:{row.Key}", _credentialNames);

        if (result.Definition is not { } definition)
            return new Entry(row.Version, null, result.Errors);

        if (!string.Equals(definition.Key, row.Key, StringComparison.OrdinalIgnoreCase))
            return new Entry(row.Version, null, [$"stored key '{row.Key}' does not match the key '{definition.Key}' inside the document"]);

        return new Entry(row.Version, Build(definition), null);
    }

    private bool Matches(IReadOnlyList<CollectionDefinitionStamp> stamps)
        => stamps.Count == _stored.Count
            && stamps.All(stamp => _stored.TryGetValue(stamp.Key, out var entry) && entry.Version == stamp.Version);

    private static ICollectionPolicy Serve(string key, Entry entry)
        => entry.Policy ?? throw new MisconfiguredCollectionException(key, entry.Errors!);

    private void LogInvalid(string key, IReadOnlyList<string> errors)
        => _logger.LogError("Collection '{Key}' is not being served; its definition is invalid:{NewLine}{Errors}",
            key, Environment.NewLine, string.Join(Environment.NewLine, errors.Select(error => $"  - {error}")));

    private ICollectionPolicy Build(FileCollectionDefinition definition) =>
        new FileCollectionPolicy(
            definition,
            definition.Enrichments.Count == 0 ? [] : [.. definition.Enrichments.Select(_enrichers.Create)],
            _administrators);

    private static string Describe(ICollectionPolicy policy)
        => policy is FileCollectionPolicy file ? $"'{file.Source}'" : policy.GetType().Name;
}
