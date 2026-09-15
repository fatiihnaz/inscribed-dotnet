using System.Text.Json.Nodes;

namespace Inscribed.Application.Services.Helpers;

public static class FileUrlRule
{
    public const string Expectation = "must be empty, an http or https address, or a path starting with '/'";

    public static bool Accepts(JsonNode? value)
    {
        if (value is not JsonObject file || file["url"] is not { } url)
            return true;

        return url is JsonValue address
            && address.TryGetValue<string>(out var text)
            && IsAllowed(text);
    }

    private static bool IsAllowed(string url) =>
        url.Length == 0
        || url.StartsWith('/')
        || url.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
}
