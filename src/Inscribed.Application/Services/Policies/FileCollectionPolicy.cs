using System.Security.Claims;
using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Policies;

public sealed class FileCollectionPolicy : ICollectionPolicy
{
    private readonly string? _slugSourceField;

    public FileCollectionPolicy(
        string key,
        CollectionSchema schema,
        SlugSource slugSource,
        string? slugSourceField,
        bool allowAnonymousRead,
        string sourceFile)
    {
        Key = key;
        Schema = schema;
        SlugSource = slugSource;
        _slugSourceField = slugSourceField;
        AllowAnonymousRead = allowAnonymousRead;
        SourceFile = sourceFile;
    }

    public string Key { get; }

    public CollectionSchema Schema { get; }

    public SlugSource SlugSource { get; }

    public bool AllowAnonymousRead { get; }

    public string SourceFile { get; }

    public bool CanEdit(ClaimsPrincipal user, string slug) => true;

    public bool CanCreate(ClaimsPrincipal user) => true;

    public string? GetSlugSourceValue(JsonNode data)
        => _slugSourceField is not null && data[_slugSourceField] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default)
        => Task.FromResult(data);
}
