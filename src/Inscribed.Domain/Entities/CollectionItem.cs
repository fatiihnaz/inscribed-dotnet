using System.Text.Json.Nodes;

namespace Inscribed.Domain.Entities;

public sealed class CollectionItem : Entity
{
    public string CollectionKey { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public JsonNode Data { get; private set; } = default!;
    public string UpdatedBy { get; private set; } = default!;
    public bool IsArchived { get; private set; }
    public DateTime? ArchivedAt { get; private set; }

    private CollectionItem() { }

    public static CollectionItem Create(
        string collectionKey,
        string slug,
        JsonNode data,
        string updatedBy,
        DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);
        ArgumentNullException.ThrowIfNull(data);

        return new CollectionItem
        {
            Id = Guid.NewGuid(),
            CollectionKey = collectionKey,
            Slug = slug,
            Data = data,
            UpdatedBy = updatedBy,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Version = 1
        };
    }

    public void UpdateData(JsonNode data, string updatedBy, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        Data = data;
        UpdatedBy = updatedBy;
        UpdatedAt = utcNow;
        Version += 1;
    }

    public void Archive(string updatedBy, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        if (IsArchived) return;

        IsArchived = true;
        ArchivedAt = utcNow;
        UpdatedBy = updatedBy;
        UpdatedAt = utcNow;
        Version += 1;
    }

    public void Restore(string updatedBy, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);

        if (!IsArchived) return;

        IsArchived = false;
        ArchivedAt = null;
        UpdatedBy = updatedBy;
        UpdatedAt = utcNow;
        Version += 1;
    }
}