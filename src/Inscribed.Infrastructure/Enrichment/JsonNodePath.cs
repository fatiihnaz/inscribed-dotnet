using System.Text.Json.Nodes;

namespace Inscribed.Infrastructure.Enrichment;

public static class JsonNodePath
{
    public static JsonNode? Select(JsonNode? root, string path)
    {
        var current = root;

        foreach (var segment in path.Split('.'))
        {
            if (current is null)
                return null;

            var name = segment;
            int? index = null;

            var bracket = segment.IndexOf('[');
            if (bracket >= 0)
            {
                name = segment[..bracket];
                index = int.Parse(segment[(bracket + 1)..^1]);
            }

            current = current is JsonObject obj && obj.TryGetPropertyValue(name, out var next) ? next : null;

            if (index is { } i)
                current = current is JsonArray array && i < array.Count ? array[i] : null;
        }

        return current;
    }
}