namespace Inscribed.Domain.Entities;

public sealed class CollectionSlugAlias
{
    public Guid Id { get; private set; }
    public string CollectionKey { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public Guid ItemId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private CollectionSlugAlias() { }

    public static CollectionSlugAlias Create(string collectionKey, string slug, Guid itemId, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return new CollectionSlugAlias
        {
            Id = Guid.NewGuid(),
            CollectionKey = collectionKey,
            Slug = slug,
            ItemId = itemId,
            CreatedAt = utcNow
        };
    }
}
