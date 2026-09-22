using Inscribed.Application.Services;
using Inscribed.Application.Services.Policies;
using Inscribed.Auth.Authorization;
using Inscribed.Cli.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Inscribed.Cli;

internal sealed class ShellCatalog
{
    private readonly IServiceScopeFactory _scopes;
    private bool _stale = true;

    public ShellCatalog(IServiceScopeFactory scopes) => _scopes = scopes;

    public IReadOnlyList<Suggestion> Clients { get; private set; } = [];

    public IReadOnlyList<Suggestion> Collections { get; private set; } = [];

    public void Invalidate() => _stale = true;

    public async Task RefreshAsync()
    {
        if (!_stale)
        {
            return;
        }

        _stale = false;

        try
        {
            using var scope = _scopes.CreateScope();
            var clients = await scope.ServiceProvider.GetRequiredService<IClientService>().ListAsync();
            var definitions = scope.ServiceProvider.GetRequiredService<ICollectionDefinitionAdminService>();
            var stored = await definitions.ListAsync();

            Clients = [.. clients.Select(client => new Suggestion(client.Key, client.IsActive ? string.Join(", ", client.Locales) : "inactive"))];
            Collections = [.. stored.Select(definition => new Suggestion(
                definition.Key,
                definitions.Validate(definition.Document, $"db:{definition.Key}").Definition?.DisplayName ?? string.Empty))];
        }
        catch (Exception)
        {
            Clients = [];
            Collections = [];
        }
    }
}

internal static class ShellCompletion
{
    private static readonly Suggestion[] SessionVerbs =
    [
        new("use", "pick a tenant for this session"),
        new("help", "list commands"),
        new("clear", "clear the screen"),
        new("exit", "leave the console"),
    ];

    public static (int Start, IReadOnlyList<Suggestion> Items) Suggest(string line, int cursor, ShellCatalog catalog)
    {
        var head = line[..cursor];
        var start = head.LastIndexOf(' ') + 1;
        var previous = head[..start].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (previous.Length > 2 && previous[^1] is "--capabilities")
        {
            start = Math.Max(start, head.LastIndexOf(',') + 1);
        }

        return (start, Filter(Candidates(previous, catalog), head[start..]));
    }

    private static IEnumerable<Suggestion> Candidates(string[] previous, ShellCatalog catalog)
    {
        if (previous.Length == 0)
        {
            return AdminCommands.Specs
                .GroupBy(spec => spec.Name.Split(' ')[0])
                .Select(group => new Suggestion(group.Key, Describe([.. group])))
                .Concat(SessionVerbs);
        }

        if (previous[0] is "use")
        {
            return previous.Length == 1 ? catalog.Clients : [];
        }

        if (previous.Length == 1)
        {
            return AdminCommands.Specs
                .Where(spec => spec.Name.StartsWith(previous[0] + " ", StringComparison.Ordinal))
                .Select(spec => new Suggestion(spec.Name[(previous[0].Length + 1)..], spec.Summary));
        }

        if (AdminCommands.Specs.FirstOrDefault(spec => spec.Name == $"{previous[0]} {previous[1]}") is not { } command)
        {
            return [];
        }

        var last = previous[^1];

        if (previous.Length > 2 && last.StartsWith("--", StringComparison.Ordinal) && Placeholder(command, last) is not null)
        {
            return Values(command.Name, last, catalog);
        }

        var used = previous.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return command.Options
            .Where(option => !used.Contains(option))
            .Select(option => new Suggestion(option, Placeholder(command, option) ?? string.Empty));
    }

    private static IEnumerable<Suggestion> Values(string command, string option, ShellCatalog catalog) => (command, option) switch
    {
        (_, "--capabilities") => Capabilities(),
        ("collection show" or "collection export" or "collection delete", "--key") => catalog.Collections,
        (_, "--client") or ("client show" or "client update", "--key") => catalog.Clients,
        (_, "--active" or "--anonymous-read") => [new("true"), new("false")],
        _ => [],
    };

    private static IEnumerable<Suggestion> Capabilities() =>
        CapabilityCatalog.Presets
            .Select(preset => new Suggestion(preset.Key, string.Join(" + ", preset.Value), Final: false))
            .Concat(CapabilityCatalog.All
                .Except(CapabilityCatalog.InstallationWide, StringComparer.Ordinal)
                .Select(capability => new Suggestion(capability, Final: false)));

    private static string Describe(IReadOnlyList<CommandSpec> group) =>
        group is [var single] && !single.Name.Contains(' ')
            ? single.Summary
            : string.Join(" · ", group.Select(spec => spec.Name[(spec.Name.IndexOf(' ') + 1)..]));

    private static string? Placeholder(CommandSpec command, string option)
    {
        var index = command.Arguments.IndexOf(option + " <", StringComparison.Ordinal);

        if (index < 0)
        {
            return null;
        }

        var open = index + option.Length + 1;
        var close = command.Arguments.IndexOf('>', open);

        return close < 0 ? null : command.Arguments[open..(close + 1)];
    }

    private static IReadOnlyList<Suggestion> Filter(IEnumerable<Suggestion> candidates, string prefix) =>
        [.. candidates
            .Where(candidate => candidate.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(candidate => candidate.Value)];
}
