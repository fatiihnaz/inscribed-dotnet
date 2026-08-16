using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Application.Services.Policies;

public sealed record StoredCollectionDefinition(string Key, JsonNode Document, string UpdatedBy, DateTime UpdatedAt, int Version);

public sealed record CollectionImportResult(string Key, bool Created, int Version);

public interface ICollectionDefinitionAdminService
{
    Task<IReadOnlyList<StoredCollectionDefinition>> ListAsync(CancellationToken cancellationToken = default);

    Task<StoredCollectionDefinition?> GetAsync(string key, CancellationToken cancellationToken = default);

    CollectionDefinitionParseResult Validate(JsonNode document, string source);

    Task<CollectionImportResult> ImportAsync(JsonNode document, string source, string updatedBy, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class CollectionDefinitionAdminService : ICollectionDefinitionAdminService
{
    private readonly ICollectionDefinitionRepository _repository;
    private readonly IReadOnlyCollection<string> _credentialNames;

    public CollectionDefinitionAdminService(ICollectionDefinitionRepository repository, EnrichmentCredentialNames credentialNames)
    {
        _repository = repository;
        _credentialNames = credentialNames.Names;
    }

    public async Task<IReadOnlyList<StoredCollectionDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _repository.ListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.Key, StringComparer.Ordinal).Select(ToStored)];
    }

    public async Task<StoredCollectionDefinition?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var row = await _repository.GetAsync(key, cancellationToken);
        return row is null ? null : ToStored(row);
    }

    public CollectionDefinitionParseResult Validate(JsonNode document, string source)
        => CollectionDefinitionParser.Parse(document, source, _credentialNames);

    public async Task<CollectionImportResult> ImportAsync(JsonNode document, string source, string updatedBy, CancellationToken cancellationToken = default)
    {
        var result = Validate(document, source);

        if (result.Definition is not { } definition)
            throw new ValidationException([.. result.Errors.Select(error => $"{source}: {error}")]);

        var utcNow = DateTime.UtcNow;
        var existing = await _repository.GetAsync(definition.Key, cancellationToken);

        if (existing is null)
        {
            var created = CollectionDefinition.Create(definition.Key, document, updatedBy, utcNow);
            await _repository.AddAsync(created, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return new CollectionImportResult(definition.Key, Created: true, created.Version);
        }

        existing.UpdateDocument(document, updatedBy, utcNow);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CollectionImportResult(definition.Key, Created: false, existing.Version);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetAsync(key, cancellationToken)
            ?? throw new NotFoundException($"Collection definition '{key}' was not found.");

        _repository.Remove(existing);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static StoredCollectionDefinition ToStored(CollectionDefinition row)
        => new(row.Key, row.Document, row.UpdatedBy, row.UpdatedAt, row.Version);
}
