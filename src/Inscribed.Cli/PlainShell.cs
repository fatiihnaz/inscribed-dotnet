using Inscribed.Auth.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Inscribed.Cli;

internal static class PlainShell
{
    public static async Task RunAsync(IServiceScopeFactory scopes, string target)
    {
        var summary = await InteractiveShell.SummarizeAsync(scopes, target);

        Output.Blank();
        Output.Note(Output.Bold("Inscribed admin console"));
        Output.Note(Output.Dim(string.Join(" · ", summary.Select(row => row.Value))));
        Output.Blank();
        Output.Note(Output.Dim("'help' for commands · 'use <client>' to pick a tenant · 'exit' to leave"));
        Output.Blank();

        string? context = null;

        while (true)
        {
            Console.Error.Write(context is null ? "inscribed> " : $"inscribed {context}> ");

            var line = Console.ReadLine();
            if (line is null)
            {
                return;
            }

            var args = InteractiveShell.Tokenize(line);
            if (args.Length == 0)
            {
                continue;
            }

            if (args[0] is "exit" or "quit" or "/exit" or "/quit")
            {
                return;
            }

            if (args[0] is "help" or "/help" or "--help" or "-h")
            {
                AdminCommands.WriteHelp(interactive: true);
                continue;
            }

            try
            {
                using var scope = scopes.CreateScope();

                if (args[0] is "use")
                {
                    context = await UseAsync(scope.ServiceProvider, args.Length > 1 ? args[1] : null);
                    continue;
                }

                await AdminCommands.RunAsync(
                    scope.ServiceProvider,
                    args,
                    new PlainInteraction(guided: args.Length <= 2, defaults: InteractiveShell.Defaults(args, context)));
            }
            catch (Exception exception)
            {
                Output.Blank();
                Output.Note(Output.Red(Output.Describe(exception)));
                Output.Blank();
            }
        }
    }

    private static async Task<string?> UseAsync(IServiceProvider services, string? key)
    {
        if (key is null)
        {
            Output.Blank();
            Output.Note(Output.Dim("Context cleared."));
            Output.Blank();
            return null;
        }

        var (resolved, name) = await InteractiveShell.ResolveTenantAsync(services, key);

        Output.Blank();
        Output.Note(Output.Dim(name is null ? $"Context: {resolved}" : $"Context: {resolved} ({name})"));
        Output.Blank();
        return resolved;
    }
}

internal sealed class PlainInteraction : IInteraction
{
    private readonly bool _guided;
    private readonly Dictionary<string, string>? _defaults;

    public PlainInteraction(bool guided, Dictionary<string, string>? defaults = null)
    {
        _guided = guided;
        _defaults = defaults;
    }

    public string? Ask(string name, bool required, string? suggestion, IReadOnlyDictionary<string, string> known)
    {
        if (_defaults is not null && _defaults.TryGetValue(name, out var preset))
        {
            return preset;
        }

        if (!required && !_guided)
        {
            return null;
        }

        if (name is "capabilities")
        {
            return AskCapabilities(required);
        }

        Console.Error.Write(suggestion is not null
            ? $"  {name} {Output.Dim($"[{suggestion}]")}: "
            : required ? $"  {name}: " : $"  {name} {Output.Dim("(optional)")}: ");

        var answer = Console.ReadLine();
        return string.IsNullOrWhiteSpace(answer) ? suggestion : answer;
    }

    public bool Confirm(string action)
    {
        Console.Error.Write($"  {action}? {Output.Dim("[y/N]")} ");
        return Console.ReadLine()?.Trim() is "y" or "Y" or "yes";
    }

    private static string? AskCapabilities(bool required)
    {
        var presets = CapabilityCatalog.Presets.ToArray();

        Console.Error.WriteLine($"  capabilities{(required ? string.Empty : " (optional)")}:");
        Console.Error.WriteLine();

        for (var index = 0; index < presets.Length; index++)
        {
            var (name, capabilities) = presets[index];
            var humanOnly = capabilities.Intersect(CapabilityCatalog.HumanOnly, StringComparer.Ordinal).Any();
            var expansion = string.Join(" + ", capabilities.Select(Output.Capability));
            var note = humanOnly ? Output.Dim("  (human only)") : string.Empty;

            Console.Error.WriteLine($"    {index + 1}  {name,-8} {expansion}{note}");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(Output.Dim("    or type them directly, comma separated"));
        Console.Error.WriteLine();
        Console.Error.Write("  > ");

        var answer = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        var chosen = answer
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, out var index) && index >= 1 && index <= presets.Length
                ? presets[index - 1].Key
                : token);

        return string.Join(',', chosen);
    }
}
