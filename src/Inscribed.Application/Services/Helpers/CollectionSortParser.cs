using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Schemas;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Application.Services.Helpers;

public static class CollectionSortParser
{
    private static readonly Dictionary<string, CollectionSortField> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slug"] = CollectionSortField.Slug,
        ["createdAt"] = CollectionSortField.CreatedAt,
        ["updatedAt"] = CollectionSortField.UpdatedAt,
    };

    public static CollectionSort Parse(CollectionSchema schema, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CollectionSort.Default;

        var parts = value.Split(':', StringSplitOptions.TrimEntries);

        if (parts.Length > 2)
            throw new ValidationException([$"Invalid sort '{value}'; expected 'field' or 'field:asc|desc'."]);

        var descending = parts.Length == 2
            ? parts[1].ToLowerInvariant() switch
            {
                "desc" => true,
                "asc" => false,
                _ => throw new ValidationException([$"Unknown sort direction '{parts[1]}'; expected 'asc' or 'desc'."]),
            }
            : false;

        if (Columns.TryGetValue(parts[0], out var column))
            return new CollectionSort(column, descending);

        var field = schema.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, parts[0], StringComparison.Ordinal));

        if (field is null)
            throw new ValidationException([$"Unknown sort field '{parts[0]}'. Available: {Available(schema)}."]);

        if (!field.Sortable)
            throw new ValidationException([$"Field '{field.Name}' is not sortable. Available: {Available(schema)}."]);

        return new CollectionSort(CollectionSortField.DataField, descending, field.Name);
    }

    private static string Available(CollectionSchema schema) =>
        string.Join(", ", Columns.Keys.Concat(schema.Fields.Where(field => field.Sortable).Select(field => field.Name)));
}
