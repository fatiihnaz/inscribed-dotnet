using System.Text.Json;
using System.Text.Json.Nodes;
using Inscribed.Application.Services.Policies;
using Inscribed.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Inscribed.Cli;

internal static partial class AdminCommands
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private static ICollectionDefinitionAdminService Definitions(IServiceProvider services)
        => services.GetRequiredService<ICollectionDefinitionAdminService>();

    private static async Task ListCollectionsAsync(ICollectionDefinitionAdminService definitions)
    {
        var table = new Table("KEY", "UPDATED BY", "UPDATED", "VERSION");
        var all = await definitions.ListAsync();

        foreach (var definition in all)
        {
            table.Add(
                definition.Key,
                definition.UpdatedBy,
                definition.UpdatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                definition.Version.ToString());
        }

        table.Write("No collection definitions; seed them from files or import one.", Count(all.Count, "definition"));
    }

    private static async Task ShowCollectionAsync(ICollectionDefinitionAdminService definitions, CommandOptions options)
    {
        var key = options.Require("key");
        var stored = await definitions.GetAsync(key)
            ?? throw new NotFoundException($"Collection definition '{key}' was not found.");

        var parsed = definitions.Validate(stored.Document, $"db:{key}");

        Output.Detail(
            ("KEY", stored.Key),
            ("STATE", parsed.Succeeded ? Output.Green("valid") : Output.Red("invalid")),
            ("UPDATED BY", stored.UpdatedBy),
            ("UPDATED", stored.UpdatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'")),
            ("VERSION", stored.Version.ToString()));

        if (!parsed.Succeeded)
        {
            Output.Blank();
            foreach (var error in parsed.Errors)
                Output.Note(Output.Red($"  - {error}"));
        }

        Output.Blank();
        Console.WriteLine(stored.Document.ToJsonString(PrettyJson));
    }

    private static void ValidateCollection(ICollectionDefinitionAdminService definitions, CommandOptions options)
    {
        var path = options.Require("file");
        var document = ReadDocument(path);
        var result = definitions.Validate(document, Path.GetFileName(path));

        if (result.Succeeded)
        {
            Console.WriteLine($"{Path.GetFileName(path)} is valid; key '{result.Definition!.Key}', {result.Definition.Schema.Fields.Count} field(s).");
            return;
        }

        throw new ValidationException([.. result.Errors.Select(error => $"{Path.GetFileName(path)}: {error}")]);
    }

    private static async Task ImportCollectionsAsync(ICollectionDefinitionAdminService definitions, CommandOptions options)
    {
        var file = options.Get("file");
        var directory = options.Get("dir");

        if (file is null == directory is null)
            throw new UsageException("Pass exactly one of --file or --dir.");

        var force = options.GetBool("force") ?? false;
        var assignLocale = options.Get("assign-locale");

        var paths = file is not null
            ? [file]
            : Directory.GetFiles(directory!, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();

        if (paths.Length == 0)
            throw new UsageException($"No .json files under '{directory}'.");

        foreach (var path in paths)
        {
            var document = ReadDocument(path);
            var name = Path.GetFileName(path);
            var impact = await definitions.ImportAsync(document, name, Environment.UserName, force, assignLocale);

            Output.Blank();
            Output.Note($"{Output.Bold(impact.Key)} {Output.Dim(impact.Creates ? "created" : "updated")} {Output.Dim($"from {name}")}");
            WriteImpact(impact);
        }

        Output.Blank();
        Output.Note(Output.Dim("Running instances keep serving the previous definitions until each one reloads."));
        Output.Blank();
    }

    private static void WriteImpact(CollectionImpact impact)
    {
        if (impact.Creates)
        {
            Output.Note(Output.Dim($"  {Count(impact.ItemCount, "existing item")} already stored under this key"));
            return;
        }

        if (impact.AddedFields.Count > 0)
            Output.Note(Output.Green($"  + {string.Join(", ", impact.AddedFields)}"));

        if (impact.RemovedFields.Count > 0)
            Output.Note(Output.Red($"  - {string.Join(", ", impact.RemovedFields)}"));

        foreach (var retyped in impact.RetypedFields)
            Output.Note(Output.Yellow($"  ~ {retyped.Name}: {retyped.From} to {retyped.To}"));

        if (impact.SlugSourceChange is { } slug)
            Output.Note(Output.Red($"  ~ slug source: {slug}"));

        foreach (var warning in impact.Warnings)
            Output.Note(Output.Yellow($"  ! {warning}"));

        var unchanged = impact.AddedFields.Count == 0
            && impact.RemovedFields.Count == 0
            && impact.RetypedFields.Count == 0
            && impact.SlugSourceChange is null
            && impact.Warnings.Count == 0;

        Output.Note(Output.Dim(unchanged
            ? $"  no schema change; {Count(impact.ItemCount, "item")} stored"
            : $"  {Count(impact.ItemCount, "item")} stored"));
    }

    private static async Task ExportCollectionAsync(ICollectionDefinitionAdminService definitions, CommandOptions options)
    {
        var key = options.Require("key");
        var stored = await definitions.GetAsync(key)
            ?? throw new NotFoundException($"Collection definition '{key}' was not found.");

        var json = stored.Document.ToJsonString(PrettyJson);

        if (options.Get("out") is not { } path)
        {
            Console.WriteLine(json);
            return;
        }

        await File.WriteAllTextAsync(path, json + Environment.NewLine);
        Console.WriteLine($"Wrote '{key}' to {path}.");
    }

    private static async Task DeleteCollectionAsync(ICollectionDefinitionAdminService definitions, CommandOptions options, IInteraction? interaction)
    {
        var key = options.Require("key");
        var force = options.GetBool("force") ?? false;

        if (interaction is not null && !interaction.Confirm($"delete the definition of '{key}'"))
            return;

        await definitions.DeleteAsync(key, force);
        Console.WriteLine($"Deleted the definition of '{key}'. Stored items were left untouched.");
    }

    private static JsonNode ReadDocument(string path)
    {
        if (!File.Exists(path))
            throw new UsageException($"File '{path}' does not exist.");

        var document = CollectionDefinitionParser.ReadDocument(File.ReadAllText(path), out var error);

        return document ?? throw new ValidationException([$"{Path.GetFileName(path)}: {error}"]);
    }
}
