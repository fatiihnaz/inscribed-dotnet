using System.Reflection;
using System.Text;
using Inscribed.Application.Services;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Issuer.Services;
using Inscribed.Cli.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Inscribed.Cli;

internal static class InteractiveShell
{
    private static readonly string[] TenantScoped = ["membership", "service-key"];

    public static Task RunAsync(IServiceScopeFactory scopes, string target) =>
        SystemTerminal.IsInteractive
            ? RunAsync(scopes, target, new SystemTerminal(Console.Error), HistoryPath())
            : PlainShell.RunAsync(scopes, target);

    public static async Task RunAsync(IServiceScopeFactory scopes, string target, ITerminal terminal, string? historyPath)
    {
        var screen = new Screen(terminal);
        var catalog = new ShellCatalog(scopes);
        var prompt = new CommandPrompt(screen, new History(historyPath), (line, cursor) => ShellCompletion.Suggest(line, cursor, catalog));
        string? context = null;

        using var capture = terminal.Capture();
        Output.Framed = true;

        try
        {
            screen.Clear();
            await WelcomeAsync(screen, scopes, target);

            while (true)
            {
                await catalog.RefreshAsync();

                var line = prompt.Read(context);
                if (line is null)
                {
                    return;
                }

                var args = Tokenize(line);
                if (args.Length == 0)
                {
                    continue;
                }

                switch (args[0])
                {
                    case "exit" or "quit" or "/exit" or "/quit":
                        return;

                    case "clear" or "/clear":
                        screen.Clear();
                        await WelcomeAsync(screen, scopes, target);
                        continue;
                }

                screen.Print($"{Output.Accent("●")} {Output.Bold(line.Trim())}");

                switch (args[0])
                {
                    case "help" or "/help" or "--help" or "-h":
                        await RunBlockAsync(screen, "help", (_, _, _) =>
                        {
                            AdminCommands.WriteHelp(interactive: true);
                            return Task.CompletedTask;
                        });
                        break;

                    case "use":
                        await RunBlockAsync(screen, "use", async (block, _, activity) =>
                            context = await UseAsync(screen, scopes, block, activity, args.Length > 1 ? args[1] : null, context));
                        break;

                    default:
                        await RunBlockAsync(screen, CommandName(args), async (block, writer, activity) =>
                        {
                            using var scope = scopes.CreateScope();
                            var interaction = new ScreenInteraction(
                                screen,
                                block,
                                activity,
                                writer,
                                scope.ServiceProvider,
                                CommandName(args),
                                guided: args.Length <= 2,
                                Defaults(args, context));

                            await AdminCommands.RunAsync(scope.ServiceProvider, args, interaction);
                        });

                        if (args[0] is "client" or "collection")
                        {
                            catalog.Invalidate();
                        }

                        break;
                }
            }
        }
        finally
        {
            screen.Clear();
            Output.Framed = false;
        }
    }

    internal static async Task<IReadOnlyList<(string Label, string Value)>> SummarizeAsync(IServiceScopeFactory scopes, string target)
    {
        var rows = new List<(string Label, string Value)> { ("database", target) };

        try
        {
            using var scope = scopes.CreateScope();

            if (scope.ServiceProvider.GetService<IAdminService>() is { } issuer)
            {
                var overview = await issuer.GetOverviewAsync();

                rows.Add(("auth", $"built-in issuer · kid {overview.SigningKeyId}"));
                rows.Add(("totals", $"{AdminCommands.Count(overview.Clients, "tenant")} · {AdminCommands.Count(overview.Users, "user")} · {AdminCommands.Count(overview.ActiveServiceKeys, "active key")}"));
            }
            else
            {
                var clients = await scope.ServiceProvider.GetRequiredService<IClientService>().ListAsync();

                rows.Add(("auth", "external issuer"));
                rows.Add(("totals", AdminCommands.Count(clients.Count, "tenant")));
            }
        }
        catch (Exception exception)
        {
            rows.Add(("error", Output.Red(Output.Describe(exception))));
        }

        return rows;
    }

    internal static async Task<(string Key, string? Name)> ResolveTenantAsync(IServiceProvider services, string key)
    {
        var client = await services.GetRequiredService<IClientService>().GetAsync(key);
        var name = services.GetService<IAdminService>() is { } issuer
            ? (await issuer.GetClientAsync(client.Key)).Name
            : null;

        return (client.Key, name);
    }

    internal static GrantTarget Target(string[] args) =>
        args[0] is "service-key" ? GrantTarget.ServiceKey : GrantTarget.Membership;

    internal static Dictionary<string, string>? Defaults(string[] args, string? context)
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

    internal static string[] Tokenize(string line)
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

    private static string CommandName(string[] args) =>
        args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? $"{args[0]} {args[1]}" : args[0];

    private static async Task WelcomeAsync(Screen screen, IServiceScopeFactory scopes, string target)
    {
        var width = screen.Width - 1;
        var mark = Wordmark.Render(width - 2);
        var tagline = $"admin console · {Version()}";
        string[] title = mark.Count > 0
            ? [Output.Dim(tagline)]
            : [$"{Output.Accent("✻")} {Output.Bold("Inscribed")} {Output.Dim(tagline)}"];
        var summary = (await SummarizeAsync(scopes, target))
            .Select(row => $"{Output.Dim(row.Label.PadRight(10))}{row.Value}")
            .ToList();

        string[] hint = [Output.Dim("help lists commands · use picks a tenant · tab completes")];

        List<string> rows =
        [
            string.Empty,
            .. Center(mark, width),
            .. mark.Count > 0 ? [string.Empty] : Array.Empty<string>(),
            .. Center(title, width),
            string.Empty,
            .. Center(summary, width),
            string.Empty,
            .. Center(hint, width),
            string.Empty,
        ];

        screen.Print(string.Join('\n', rows));
    }

    private static IEnumerable<string> Center(IReadOnlyList<string> block, int inner)
    {
        if (block.Count == 0)
        {
            return [];
        }

        var offset = new string(' ', Math.Max(0, (inner - block.Max(Output.VisibleLength)) / 2));

        return block.Select(line => offset + line);
    }

    private static string Version()
    {
        var assembly = typeof(InteractiveShell).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "dev";
        var metadata = version.IndexOf('+');

        return metadata > 0 ? version[..metadata] : version;
    }

    private static async Task<string?> UseAsync(Screen screen, IServiceScopeFactory scopes, Block block, Activity activity, string? key, string? current)
    {
        using var scope = scopes.CreateScope();

        if (key is null)
        {
            var clients = await scope.ServiceProvider.GetRequiredService<IClientService>().ListAsync();

            List<MenuItem> items =
            [
                new(string.Empty, "none", Output.Dim("clear the tenant")),
                .. clients.Select(client => new MenuItem(client.Key, client.Key, client.IsActive ? string.Empty : Output.Red("inactive"))),
            ];

            using (activity.Suspend())
            {
                var initial = Math.Max(0, items.FindIndex(item => item.Value == current));
                var picked = new SelectMenu("tenant", items, multiple: false, block.Prefix, Block.Indent, initial).Run(screen)
                    ?? throw new PromptCancelledException();

                key = picked[0].Value;
            }

            if (key.Length == 0)
            {
                block.Line("Context cleared.");
                return null;
            }
        }

        var (resolved, name) = await ResolveTenantAsync(scope.ServiceProvider, key);
        block.Line(name is null ? $"Context: {resolved}" : $"Context: {resolved} {Output.Dim($"({name})")}");
        return resolved;
    }

    private static async Task RunBlockAsync(Screen screen, string label, Func<Block, BlockWriter, Activity, Task> body)
    {
        var block = new Block(screen);
        var writer = new BlockWriter(block);
        var output = Console.Out;
        var error = Console.Error;

        using (var activity = new Activity(screen, label))
        {
            Console.SetOut(writer);
            Console.SetError(writer);

            try
            {
                await body(block, writer, activity);
            }
            catch (PromptCancelledException)
            {
                writer.Complete();
                block.Line(Output.Dim("Cancelled."));
            }
            catch (Exception exception)
            {
                writer.Complete();

                foreach (var message in Output.Describe(exception).Split('\n'))
                {
                    block.Line(Output.Red(message.TrimEnd('\r')));
                }
            }
            finally
            {
                writer.Complete();
                Console.SetOut(output);
                Console.SetError(error);
            }
        }

        if (!block.Started)
        {
            block.Line(Output.Dim("(no output)"));
        }

        screen.Print(string.Empty);
    }

    private static string? HistoryPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return root.Length == 0 ? null : Path.Combine(root, "inscribed", "history");
    }
}
