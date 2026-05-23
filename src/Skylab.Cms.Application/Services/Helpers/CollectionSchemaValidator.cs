using System.Text.Json.Nodes;
using Skylab.Cms.Application.Contracts.Schemas;
using Skylab.Cms.Domain.Exceptions;

namespace Skylab.Cms.Application.Services.Helpers;

public static class CollectionSchemaValidator
{
    public static JsonObject ValidateAndStrip(CollectionSchema schema, JsonNode data)
    {
        if (data is not JsonObject incoming)
            throw new ValidationException(["Data must be a JSON object."]);

        var errors = new List<string>();
        var result = new JsonObject();
        var fieldsByName = schema.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            if (field.ReadOnly) continue;

            var hasValue = incoming.TryGetPropertyValue(field.Name, out var value) && value is not null;

            if (!hasValue)
            {
                if (field.Required)
                    errors.Add($"Field '{field.Name}' is required.");
                continue;
            }

            if (!IsValidForType(value!, field, out var typeError))
            {
                errors.Add($"Field '{field.Name}': {typeError}");
                continue;
            }

            result[field.Name] = value!.DeepClone();
        }

        foreach (var prop in incoming)
        {
            if (!fieldsByName.ContainsKey(prop.Key))
                errors.Add($"Unknown field '{prop.Key}'.");
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);

        return result;
    }

    private static bool IsValidForType(JsonNode value, FieldDefinition field, out string error)
    {
        error = string.Empty;

        switch (field.Type)
        {
            case FieldType.Text:
            case FieldType.RichText:
            case FieldType.Url:
                if (value is not JsonValue tv || tv.GetValueKind() != System.Text.Json.JsonValueKind.String)
                { error = "expected string."; return false; }
                if (field.Options is { Count: > 0 } && !field.Options.Contains(tv.GetValue<string>()))
                { error = "value not in allowed options."; return false; }
                return true;

            case FieldType.Bool:
                if (value is not JsonValue bv ||
                    (bv.GetValueKind() != System.Text.Json.JsonValueKind.True && bv.GetValueKind() != System.Text.Json.JsonValueKind.False))
                { error = "expected boolean."; return false; }
                return true;

            case FieldType.Number:
                if (value is not JsonValue nv || nv.GetValueKind() != System.Text.Json.JsonValueKind.Number)
                { error = "expected number."; return false; }
                return true;

            case FieldType.Date:
                if (value is not JsonValue dv || dv.GetValueKind() != System.Text.Json.JsonValueKind.String
                    || !DateTime.TryParse(dv.GetValue<string>(), out _))
                { error = "expected ISO date string."; return false; }
                return true;

            case FieldType.StringArray:
                if (value is not JsonArray arr)
                { error = "expected array."; return false; }
                foreach (var item in arr)
                {
                    if (item is not JsonValue iv || iv.GetValueKind() != System.Text.Json.JsonValueKind.String)
                    { error = "expected array of strings."; return false; }
                }
                return true;

            default:
                error = $"unsupported type {field.Type}.";
                return false;
        }
    }
}
