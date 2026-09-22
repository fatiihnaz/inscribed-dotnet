using Inscribed.Application.Services;
using Inscribed.Application.Services.Policies;
using Inscribed.Auth.Issuer.Services;
using Inscribed.Cli.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Inscribed.Cli;

internal sealed class ScreenInteraction : IInteraction
{
    private readonly Screen _screen;
    private readonly Block _block;
    private readonly Activity _activity;
    private readonly BlockWriter _writer;
    private readonly IServiceProvider _services;
    private readonly string _command;
    private readonly bool _guided;
    private readonly Dictionary<string, string>? _defaults;

    public ScreenInteraction(
        Screen screen,
        Block block,
        Activity activity,
        BlockWriter writer,
        IServiceProvider services,
        string command,
        bool guided,
        Dictionary<string, string>? defaults)
    {
        _screen = screen;
        _block = block;
        _activity = activity;
        _writer = writer;
        _services = services;
        _command = command;
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

        using var pause = _activity.Suspend();
        _writer.Complete();

        if (name is "capabilities")
        {
            return CapabilityPicker.Pick(_screen, _block, InteractiveShell.Target(_command.Split(' ')), required);
        }

        var choices = Choices(name, known);

        if (choices.Any(choice => choice.Disabled is null))
        {
            var picked = new SelectMenu(name, choices, multiple: false, _block.Prefix, Block.Indent).Run(_screen)
                ?? throw new PromptCancelledException();

            _block.Line($"{name}: {picked[0].Label}");
            return picked[0].Value.Length == 0 ? null : picked[0].Value;
        }

        var answer = TextPrompt.Read(_screen, _block.Prefix, Block.Indent, name, suggestion, optional: !required);
        _block.Line($"{name}: {answer ?? Output.Dim("(none)")}");
        return answer;
    }

    public bool Confirm(string action)
    {
        using var pause = _activity.Suspend();
        _writer.Complete();

        var menu = new SelectMenu(
            $"{action}?",
            [new MenuItem("yes", "Yes", Hotkey: 'y'), new MenuItem("no", "No", Hotkey: 'n')],
            multiple: false,
            _block.Prefix,
            Block.Indent,
            initial: 1);

        var confirmed = menu.Run(_screen) is [{ Value: "yes" }];
        _block.Line($"{action}? {(confirmed ? "yes" : "no")}");
        return confirmed;
    }

    private IReadOnlyList<MenuItem> Choices(string name, IReadOnlyDictionary<string, string> known)
    {
        switch (name, _command)
        {
            case ("client", _):
            case ("key", "client show" or "client update"):
                return Clients();

            case ("key", "collection show" or "collection export" or "collection delete"):
                return Collections();

            case ("email", "membership set"):
                return Users();

            case ("email", "membership remove") when known.TryGetValue("client", out var client):
                return Members(client);

            case ("id", "service-key revoke") when known.TryGetValue("client", out var owner):
                return ServiceKeys(owner);

            case ("active" or "anonymous-read", _):
                return [new(string.Empty, "keep current"), new("true", "true"), new("false", "false")];

            case ("force", _):
                return [new(string.Empty, "no"), new("true", "yes")];

            default:
                return [];
        }
    }

    private IReadOnlyList<MenuItem> Clients()
    {
        var clients = _services.GetRequiredService<IClientService>().ListAsync().GetAwaiter().GetResult();

        return [.. clients.Select(client => new MenuItem(
            client.Key,
            client.Key,
            client.IsActive ? Output.Dim(string.Join(", ", client.Locales)) : Output.Red("inactive")))];
    }

    private IReadOnlyList<MenuItem> Collections()
    {
        var definitions = _services.GetRequiredService<ICollectionDefinitionAdminService>();

        return [.. definitions.ListAsync().GetAwaiter().GetResult().Select(stored => new MenuItem(
            stored.Key,
            stored.Key,
            Output.Dim(definitions.Validate(stored.Document, $"db:{stored.Key}").Definition?.DisplayName ?? string.Empty)))];
    }

    private IReadOnlyList<MenuItem> Users()
    {
        if (_services.GetService<IAdminService>() is not { } admin)
        {
            return [];
        }

        return [.. admin.ListUsersAsync().GetAwaiter().GetResult().Select(user => new MenuItem(
            user.Email,
            user.Email,
            user.IsActive ? Output.Dim(user.DisplayName) : Output.Red("inactive")))];
    }

    private IReadOnlyList<MenuItem> Members(string client)
    {
        if (_services.GetService<IAdminService>() is not { } admin)
        {
            return [];
        }

        return [.. admin.ListMembershipsAsync(client).GetAwaiter().GetResult().Select(member => new MenuItem(
            member.Email,
            member.Email,
            Output.Capabilities(member.Capabilities)))];
    }

    private IReadOnlyList<MenuItem> ServiceKeys(string client)
    {
        if (_services.GetService<IAdminService>() is not { } admin)
        {
            return [];
        }

        var now = DateTime.UtcNow;

        return [.. admin.ListServiceKeysAsync(client).GetAwaiter().GetResult()
            .OrderByDescending(key => key.IsActive(now))
            .Select(key => new MenuItem(
                key.Id.ToString(),
                key.Id.ToString()[..8],
                $"{key.Name}  {Output.Capabilities(key.Roles)}",
                Disabled: key.IsActive(now) ? null : key.RevokedAt is not null ? "revoked" : "expired"))];
    }
}
