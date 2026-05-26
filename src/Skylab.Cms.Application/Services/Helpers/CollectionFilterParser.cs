using System.Globalization;
using System.Text.Json.Nodes;
using Skylab.Cms.Application.Contracts.Schemas;
using Skylab.Cms.Domain.Exceptions;

namespace Skylab.Cms.Application.Services.Helpers;

public static class CollectionFilterParser
{
    public static JsonObject Build(CollectionSchema schema, IDictionary<string, string> filters)
    {
        var result = new JsonObject();
        var errors = new List<string>();
        var fieldsByName = schema.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

        foreach (var (name, value) in filters)
        {
            if (!fieldsByName.TryGetValue(name, out var field))
            {
                errors.Add($"Unknown filter field '{name}'.");
                continue;
            }

            if (!field.Filterable)
            {
                errors.Add($"Field '{name}' is not filterable.");
                continue;
            }

            try
            {
                JsonNode node = field.Type switch
                {
                    FieldType.StringArray => new JsonArray(JsonValue.Create(value)),
                    FieldType.Bool => JsonValue.Create(bool.Parse(value)),
                    FieldType.Number => JsonValue.Create(double.Parse(value, CultureInfo.InvariantCulture)),
                    _ => JsonValue.Create(value)
                };
                result[name] = node;
            }
            catch (FormatException)
            {
                errors.Add($"Field '{name}': invalid value '{value}' for type {field.Type}.");
            }
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);

        return result;
    }
}
