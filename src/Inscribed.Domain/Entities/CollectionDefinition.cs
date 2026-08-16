using System.Text.Json.Nodes;

namespace Inscribed.Domain.Entities;

public sealed class CollectionDefinition : Entity
{
    public string Key { get; private set; } = default!;
    public JsonNode Document { get; private set; } = default!;
    public string UpdatedBy { get; private set; } = default!;

    private CollectionDefinition() { }

    public static CollectionDefinition Create(string key, JsonNode document, string updatedBy, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);
        ArgumentNullException.ThrowIfNull(document);

        return new CollectionDefinition
        {
            Id = Guid.NewGuid(),
            Key = key,
            Document = document,
            UpdatedBy = updatedBy,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public void UpdateDocument(JsonNode document, string updatedBy, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        Document = document;
        UpdatedBy = updatedBy;
        UpdatedAt = utcNow;
        Version += 1;
    }
}
