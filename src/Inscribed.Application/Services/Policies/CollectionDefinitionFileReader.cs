using System.Text.Json.Nodes;

namespace Inscribed.Application.Services.Policies;

public sealed record CollectionDefinitionFile(string FileName, JsonNode Document, FileCollectionDefinition Definition);

public static class CollectionDefinitionFileReader
{
    public static IReadOnlyList<CollectionDefinitionFile> Load(string directory, bool required, IReadOnlyCollection<string> credentialNames)
    {
        if (!Directory.Exists(directory))
        {
            if (required)
                throw new InvalidOperationException($"Collections path '{directory}' does not exist.");

            return [];
        }

        var errors = new List<string>();
        var files = new List<CollectionDefinitionFile>();
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(path);
            var document = CollectionDefinitionParser.ReadDocument(File.ReadAllText(path), out var readError);

            if (document is null)
            {
                errors.Add($"{fileName}: {readError}");
                continue;
            }

            var result = CollectionDefinitionParser.Parse(document, fileName, credentialNames);
            var fileErrors = new List<string>(result.Errors);

            if (result.Definition is { } parsed && sources.TryGetValue(parsed.Key, out var otherFile))
                fileErrors.Add($"duplicate collection key '{parsed.Key}', already defined in '{otherFile}'");

            if (fileErrors.Count > 0)
            {
                errors.AddRange(fileErrors.Select(error => $"{fileName}: {error}"));
                continue;
            }

            sources.Add(result.Definition!.Key, fileName);
            files.Add(new CollectionDefinitionFile(fileName, document, result.Definition));
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Invalid collection definition(s) in '{directory}':{Environment.NewLine}" +
                string.Join(Environment.NewLine, errors.Select(error => $"  - {error}")));

        return files;
    }
}
