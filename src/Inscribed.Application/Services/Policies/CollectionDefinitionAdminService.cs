using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Schemas;
using Inscribed.Domain.Entities;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Application.Services.Policies;

public sealed record StoredCollectionDefinition(string Key, JsonNode Document, string UpdatedBy, DateTime UpdatedAt, int Version);

public sealed record RetypedField(string Name, FieldType From, FieldType To);

public sealed record CollectionImpact(
    string Key,
    bool Creates,
    IReadOnlyList<string> AddedFields,
    IReadOnlyList<string> RemovedFields,
    IReadOnlyList<RetypedField> RetypedFields,
    string? SlugSourceChange,
    IReadOnlyList<string> Warnings,
    int ItemCount)
{
    public bool Destructive => RemovedFields.Count > 0 || RetypedFields.Count > 0 || SlugSourceChange is not null;

    public bool NeedsForce => Destructive && ItemCount > 0;
}

public interface ICollectionDefinitionAdminService
{
    Task<IReadOnlyList<StoredCollectionDefinition>> ListAsync(CancellationToken cancellationToken = default);

    Task<StoredCollectionDefinition?> GetAsync(string key, CancellationToken cancellationToken = default);

    CollectionDefinitionParseResult Validate(JsonNode document, string source);

    Task<CollectionImpact> PreviewAsync(JsonNode document, string source, CancellationToken cancellationToken = default);

    Task<CollectionImpact> ImportAsync(
        JsonNode document,
        string source,
        string updatedBy,
        bool force = false,
        string? assignLocale = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, bool force = false, CancellationToken cancellationToken = default);
}

public sealed class CollectionDefinitionAdminService : ICollectionDefinitionAdminService
{
    private readonly ICollectionDefinitionRepository _definitions;
    private readonly ICollectionItemRepository _items;
    private readonly IReadOnlyCollection<string> _credentialNames;

    public CollectionDefinitionAdminService(
        ICollectionDefinitionRepository definitions,
        ICollectionItemRepository items,
        EnrichmentCredentialNames credentialNames)
    {
        _definitions = definitions;
        _items = items;
        _credentialNames = credentialNames.Names;
    }

    public async Task<IReadOnlyList<StoredCollectionDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _definitions.ListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.Key, StringComparer.Ordinal).Select(ToStored)];
    }

    public async Task<StoredCollectionDefinition?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var row = await _definitions.GetAsync(key, cancellationToken);
        return row is null ? null : ToStored(row);
    }

    public CollectionDefinitionParseResult Validate(JsonNode document, string source)
        => CollectionDefinitionParser.Parse(document, source, _credentialNames);

    public async Task<CollectionImpact> PreviewAsync(JsonNode document, string source, CancellationToken cancellationToken = default)
    {
        var definition = Require(document, source);
        var existing = await _definitions.GetAsync(definition.Key, cancellationToken);

        return await BuildImpactAsync(definition, existing, cancellationToken);
    }

    public async Task<CollectionImpact> ImportAsync(
        JsonNode document,
        string source,
        string updatedBy,
        bool force = false,
        string? assignLocale = null,
        CancellationToken cancellationToken = default)
    {
        var definition = Require(document, source);
        var existing = await _definitions.GetAsync(definition.Key, cancellationToken);
        var impact = await BuildImpactAsync(definition, existing, cancellationToken);

        if (impact.NeedsForce && !force)
            throw new ValidationException([$"Importing '{definition.Key}' drops or retypes fields that {impact.ItemCount} stored item(s) may rely on. Re-run with force to proceed."]);

        if (assignLocale is not null && !definition.Locales.Contains(assignLocale, StringComparer.Ordinal))
            throw new ValidationException([$"Locale '{assignLocale}' is not declared by '{definition.Key}'."]);

        var utcNow = DateTime.UtcNow;

        if (existing is null)
            await _definitions.AddAsync(CollectionDefinition.Create(definition.Key, document, updatedBy, utcNow), cancellationToken);
        else
            existing.UpdateDocument(document, updatedBy, utcNow);

        await _definitions.SaveChangesAsync(cancellationToken);

        if (assignLocale is not null)
            await _items.AssignMissingLocaleAsync(definition.Key, assignLocale, cancellationToken);

        return impact;
    }

    public async Task DeleteAsync(string key, bool force = false, CancellationToken cancellationToken = default)
    {
        var existing = await _definitions.GetAsync(key, cancellationToken)
            ?? throw new NotFoundException($"Collection definition '{key}' was not found.");

        var items = await _items.CountAsync(key, cancellationToken);

        if (items > 0 && !force)
            throw new ValidationException([$"Collection '{key}' still holds {items} item(s), which stay in the database and become unreachable. Re-run with force to proceed."]);

        _definitions.Remove(existing);
        await _definitions.SaveChangesAsync(cancellationToken);
    }

    private FileCollectionDefinition Require(JsonNode document, string source)
    {
        var result = Validate(document, source);

        return result.Definition
            ?? throw new ValidationException([.. result.Errors.Select(error => $"{source}: {error}")]);
    }

    private async Task<CollectionImpact> BuildImpactAsync(
        FileCollectionDefinition incoming,
        CollectionDefinition? existing,
        CancellationToken cancellationToken)
    {
        var itemCount = await _items.CountAsync(incoming.Key, cancellationToken);

        if (existing is null)
            return new CollectionImpact(incoming.Key, Creates: true, [], [], [], null, [], itemCount);

        var previous = CollectionDefinitionParser.Parse(existing.Document, $"db:{existing.Key}", _credentialNames).Definition;

        if (previous is null)
            return new CollectionImpact(
                incoming.Key,
                Creates: false,
                [], [], [], null,
                ["the stored definition is invalid, so no field-level comparison was possible"],
                itemCount);

        var before = Stored(previous);
        var after = Stored(incoming);

        var added = after.Keys.Except(before.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var removed = before.Keys.Except(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        var retyped = before
            .Where(field => after.TryGetValue(field.Key, out var now) && now != field.Value)
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => new RetypedField(field.Key, field.Value, after[field.Key]))
            .ToArray();

        var slugChange = previous.SlugSource != incoming.SlugSource
            ? $"{previous.SlugSource} to {incoming.SlugSource}"
            : null;

        return new CollectionImpact(
            incoming.Key,
            Creates: false,
            added,
            removed,
            retyped,
            slugChange,
            LocaleWarnings(previous, incoming),
            itemCount);
    }

    private static IReadOnlyList<string> LocaleWarnings(FileCollectionDefinition previous, FileCollectionDefinition incoming)
    {
        var warnings = new List<string>();

        if (previous.Locales.Count == 0 && incoming.Locales.Count > 0)
            warnings.Add("this collection was not localized, so every stored item has no locale and will be missing from locale-filtered reads until it is backfilled");

        if (previous.Locales.Count > 0 && incoming.Locales.Count == 0)
            warnings.Add("this collection was localized, so items written in different languages will now be listed together");

        var dropped = previous.Locales.Except(incoming.Locales, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        if (incoming.Locales.Count > 0 && dropped.Length > 0)
            warnings.Add($"locale(s) {string.Join(", ", dropped)} are no longer declared, so items stored under them become unreachable");

        return warnings;
    }

    private static Dictionary<string, FieldType> Stored(FileCollectionDefinition definition)
        => definition.Schema.Fields
            .Where(field => !field.Computed)
            .ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal);

    private static StoredCollectionDefinition ToStored(CollectionDefinition row)
        => new(row.Key, row.Document, row.UpdatedBy, row.UpdatedAt, row.Version);
}
