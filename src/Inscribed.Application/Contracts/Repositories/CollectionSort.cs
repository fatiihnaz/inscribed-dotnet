namespace Inscribed.Application.Contracts.Repositories;

public enum CollectionSortField
{
    Slug,
    CreatedAt,
    UpdatedAt,
    DataField,
}

public sealed record CollectionSort(
    CollectionSortField Field,
    bool Descending,
    string? DataField = null)
{
    public static readonly CollectionSort Default = new(CollectionSortField.Slug, Descending: false);
}
