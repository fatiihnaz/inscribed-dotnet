using System.Text;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Inscribed.Cli;

internal static class InteractiveShell
{
    private static readonly string[] TenantScoped = ["membership", "service-key"];

    private static readonly string[] SessionVerbs = ["use", "help", "exit"];

    public static async Task RunAsync(IServiceScopeFactory scopes, string target)
    {
        await WelcomeAsync(scopes, target);

        string? context = null;
        string[]? clientKeys = null;

        string[] ClientKeys()
        {
            if (clientKeys is null)
            {
                using var scope = scopes.CreateScope();
                var clients = scope.ServiceProvider.GetRequiredService<IAdminService>().ListClientsAsync().GetAwaiter().GetResult();
                clientKeys = [.. clients.Select(client => client.Key)];
            }

            return clientKeys;
        }

        var editor = new LineEditor((line, cursor) => Complete(line, cursor, ClientKeys));

        while (true)
        {
            var line = editor.Read(context is null ? "inscribed> " : $"inscribed {context}> ");
            if (line is null)
            {
                return;
            }

            var args = Tokenize(line);
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

            if (args[0] is "use")
            {
                context = await UseAsync(scopes, args.Length > 1 ? args[1] : null);
                continue;
            }

            if (args[0] is "client")
            {
                clientKeys = null;
            }

            try
            {
                using var scope = scopes.CreateScope();
                await AdminCommands.RunAsync(
                    scope.ServiceProvider.GetRequiredService<IAdminService>(),
                    args,
                    new ShellInteraction(guided: args.Length <= 2, defaults: Defaults(args, context)));
            }
            catch (Exception exception)
            {
                Output.Blank();
                Output.Note(Output.Red(exception.Message));
                Output.Blank();
            }
        }
    }

    private static async Task WelcomeAsync(IServiceScopeFactory scopes, string target)
    {
        Output.Blank();
        Output.Note(Output.Bold("Inscribed admin console"));

        try
        {
            using var scope = scopes.CreateScope();
            var overview = await scope.ServiceProvider.GetRequiredService<IAdminService>().GetOverviewAsync();
            Output.Note(Output.Dim($"{target} · {overview.Clients} clients · {overview.Users} users · {overview.ActiveServiceKeys} active keys · kid {overview.SigningKeyId}"));
        }
        catch (Exception exception)
        {
            Output.Note(Output.Red($"{target} · {exception.Message}"));
        }

        Output.Blank();
        Output.Note(Output.Dim("'help' for commands · 'use <client>' to pick a tenant · 'exit' to leave"));
        Output.Blank();
    }

    private static async Task<string?> UseAsync(IServiceScopeFactory scopes, string? key)
    {
        if (key is null)
        {
            Output.Blank();
            Output.Note(Output.Dim("Context cleared."));
            Output.Blank();
            return null;
        }

        try
        {
            using var scope = scopes.CreateScope();
            var detail = await scope.ServiceProvider.GetRequiredService<IAdminService>().GetClientAsync(key);

            Output.Blank();
            Output.Note(Output.Dim($"Context: {detail.Client.Key} ({detail.Client.Name})"));
            Output.Blank();
            return detail.Client.Key;
        }
        catch (Exception exception)
        {
            Output.Blank();
            Output.Note(Output.Red(exception.Message));
            Output.Blank();
            return null;
        }
    }

    private static IReadOnlyList<string> Complete(string line, int cursor, Func<string[]> clientKeys)
    {
        var tokens = line[..cursor].Split(' ');
        var index = tokens.Length - 1;
        var current = tokens[index];

        if (index == 0)
        {
            return Filter(SessionVerbs.Concat(AdminCommands.Specs.Select(spec => spec.Name.Split(' ')[0])), current);
        }

        if (index == 1)
        {
            return tokens[0] is "use"
                ? Filter(clientKeys(), current)
                : Filter(
                    AdminCommands.Specs
                        .Where(spec => spec.Name.StartsWith(tokens[0] + " ", StringComparison.Ordinal))
                        .Select(spec => spec.Name.Split(' ')[1]),
                    current);
        }

        if (AdminCommands.Specs.FirstOrDefault(spec => spec.Name == $"{tokens[0]} {tokens[1]}") is not { } command)
        {
            return [];
        }

        var previous = tokens[index - 1];

        if (previous.StartsWith("--", StringComparison.Ordinal))
        {
            return previous switch
            {
                "--client" or "--key" => Filter(clientKeys(), current),
                "--capabilities" => Filter(CapabilityCatalog.All.Concat(CapabilityCatalog.Presets.Keys), current),
                _ => [],
            };
        }

        var used = tokens[..index].Where(token => token.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Filter(command.Options.Where(option => !used.Contains(option)), current);
    }

    private static string[] Filter(IEnumerable<string> candidates, string prefix) =>
        [.. candidates.Where(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct().Order()];

    private static Dictionary<string, string>? Defaults(string[] args, string? context)
    {
        if (context is null)
        {
            return null;
        }

        if (TenantScoped.Contains(args[0]))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["client"] = context };
        }

        var action = args.Length > 1 ? args[1] : string.Empty;
        return args[0] is "client" && action is "show" or "update"
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["key"] = context }
            : null;
    }

    private static string[] Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var character in line)
        {
            if (character is '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return [.. tokens];
    }
}

internal sealed class ShellInteraction : IInteraction
{
    private readonly bool _guided;
    private readonly Dictionary<string, string>? _defaults;

    public ShellInteraction(bool guided, Dictionary<string, string>? defaults = null)
    {
        _guided = guided;
        _defaults = defaults;
    }

    public string? Ask(string name, bool required, string? suggestion = null)
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
