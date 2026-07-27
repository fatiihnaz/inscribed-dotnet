using System.Text;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Entities;
using Inscribed.Auth.Services;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Cli;

internal sealed record CommandSpec(string Name, string Section, string Arguments, string[] Options);

internal static class AdminCommands
{
    private const int MaxKeyLength = 64;

    private const int StaleDays = 90;

    private static readonly Dictionary<char, char> TurkishLetters = new()
    {
        ['ç'] = 'c', ['Ç'] = 'c',
        ['ğ'] = 'g', ['Ğ'] = 'g',
        ['ı'] = 'i', ['I'] = 'i',
        ['İ'] = 'i',
        ['ö'] = 'o', ['Ö'] = 'o',
        ['ş'] = 's', ['Ş'] = 's',
        ['ü'] = 'u', ['Ü'] = 'u',
    };

    public static readonly CommandSpec[] Specs =
    [
        new("client list", "TENANTS", "", []),
        new("client show", "TENANTS", "--key <key>", ["--key"]),
        new("client create", "TENANTS", "--key <key> --name <name> [--origins <a,b>]", ["--key", "--name", "--origins"]),
        new("client update", "TENANTS", "--key <key> --name <name> [--origins <a,b>] [--active <bool>] [--anonymous-read <bool>]", ["--key", "--name", "--origins", "--active", "--anonymous-read"]),
        new("user list", "PEOPLE", "", []),
        new("membership list", "PEOPLE", "--client <key>", ["--client"]),
        new("membership set", "PEOPLE", "--client <key> --email <email> [--capabilities <a,b>]", ["--client", "--email", "--capabilities"]),
        new("membership remove", "PEOPLE", "--client <key> --email <email>", ["--client", "--email"]),
        new("service-key list", "KEYS", "--client <key>", ["--client"]),
        new("service-key create", "KEYS", "--client <key> --name <name> --capabilities <a,b> [--expires <date>]", ["--client", "--name", "--capabilities", "--expires"]),
        new("service-key revoke", "KEYS", "--client <key> --id <id or prefix>", ["--client", "--id"]),
        new("signing-key rotate", "KEYS", "", []),
        new("status", "SESSION", "", []),
    ];

    public static readonly string[] Commands = [.. Specs.Select(spec => spec.Name)];

    public static void WriteHelp(bool interactive)
    {
        var width = Specs.Max(spec => spec.Name.Length) + 2;

        Output.Blank();
        Output.Note(Output.Bold("Inscribed admin console"));

        foreach (var section in Specs.Select(spec => spec.Section).Distinct())
        {
            Output.Blank();
            Output.Note(Output.Dim(section));

            foreach (var spec in Specs.Where(spec => spec.Section == section))
            {
                Output.Note($"  {spec.Name.PadRight(width)}{Output.Dim(spec.Arguments)}".TrimEnd());
            }

            if (section is "SESSION" && interactive)
            {
                Output.Note($"  {"use <client>".PadRight(width)}{Output.Dim("fix a tenant so --client and --key fill in; 'use' alone clears")}");
                Output.Note("  help · exit");
            }
        }

        Output.Blank();
        Output.Note(Output.Dim("CAPABILITIES"));
        Output.Note("  " + string.Join("  ", CapabilityCatalog.All.Select(Output.Capability)));

        var presetWidth = CapabilityCatalog.Presets.Max(preset => preset.Key.Length) + 2;

        foreach (var preset in CapabilityCatalog.Presets)
        {
            Output.Note($"  {preset.Key.PadRight(presetWidth)}{Output.Dim("= ")}{string.Join(Output.Dim(" + "), preset.Value.Select(Output.Capability))}");
        }

        Output.Blank();
        Output.Note(Output.Dim("Membership capabilities replace the existing set. A service key needs at least one"));
        Output.Note(Output.Dim("capability and can never hold tenant:admin."));

        if (!interactive)
        {
            Output.Note(Output.Dim("Run without arguments for an interactive console."));
            Output.Note(Output.Dim("Set ConnectionStrings__Default to the Inscribed database before running."));
        }

        Output.Blank();
    }

    public static async Task RunAsync(IAdminService admin, string[] args, IInteraction? interaction = null)
    {
        var group = args[0];
        var action = args.Length > 1 ? args[1] : string.Empty;
        var options = CommandOptions.Parse(args, 2, interaction);

        switch (group, action)
        {
            case ("status", ""):
                await ShowStatusAsync(admin);
                return;
            case ("user", "list"):
                await ListUsersAsync(admin);
                return;
            case ("client", "list"):
                await ListClientsAsync(admin);
                return;
            case ("client", "show"):
                await ShowClientAsync(admin, options);
                return;
            case ("client", "create"):
                await CreateClientAsync(admin, options);
                return;
            case ("client", "update"):
                await UpdateClientAsync(admin, options);
                return;
            case ("membership", "list"):
                await ListMembershipsAsync(admin, options);
                return;
            case ("membership", "set"):
                await SetMembershipAsync(admin, options);
                return;
            case ("membership", "remove"):
                await RemoveMembershipAsync(admin, options, interaction);
                return;
            case ("service-key", "list"):
                await ListServiceKeysAsync(admin, options);
                return;
            case ("service-key", "create"):
                await CreateServiceKeyAsync(admin, options);
                return;
            case ("service-key", "revoke"):
                await RevokeServiceKeyAsync(admin, options, interaction);
                return;
            case ("signing-key", "rotate"):
                RotateSigningKey(admin, interaction);
                return;
            default:
                throw new UsageException(Unknown(group, action));
        }
    }

    private static string Unknown(string group, string action)
    {
        var typed = string.Join(' ', new[] { group, action }.Where(part => part.Length > 0));
        var nearest = Commands
            .Select(command => (Command: command, Distance: Distance(command, typed)))
            .OrderBy(candidate => candidate.Distance)
            .First();

        return nearest.Distance <= 3
            ? $"Unknown command '{typed}'. Did you mean '{nearest.Command}'?"
            : $"Unknown command '{typed}'.";
    }

    private static int Distance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static async Task ListUsersAsync(IAdminService admin)
    {
        var table = new Table("EMAIL", "NAME", "GOOGLE", "STATE");
        var users = await admin.ListUsersAsync();

        foreach (var user in users)
        {
            table.Add(
                user.Email,
                user.DisplayName,
                user.GoogleSubject is null ? Output.Yellow("unlinked") : "linked",
                user.IsActive ? Output.Green("active") : Output.Red("inactive"));
        }

        table.Write("No users yet; they are created on first login.", Count(users.Count, "user"));
    }

    private static async Task ShowStatusAsync(IAdminService admin)
    {
        var overview = await admin.GetOverviewAsync();

        Output.Detail(
            ("CLIENTS", overview.Clients.ToString()),
            ("USERS", overview.Users.ToString()),
            ("ACTIVE KEYS", overview.ActiveServiceKeys.ToString()),
            ("SIGNING KEY", overview.SigningKeyId));
    }

    private static async Task ShowClientAsync(IAdminService admin, CommandOptions options)
    {
        var detail = await admin.GetClientAsync(options.Require("key"));
        var client = detail.Client;

        Output.Detail(
            ("KEY", client.Key),
            ("NAME", client.Name),
            ("STATE", client.IsActive ? Output.Green("active") : Output.Red("inactive")),
            ("ANON-READ", client.AllowAnonymousContentRead ? "yes" : "no"),
            ("ORIGINS", client.AllowedRedirectOrigins.Length == 0 ? Output.Dim("(none)") : string.Join(", ", client.AllowedRedirectOrigins)),
            ("CREATED", client.CreatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'")),
            ("KEYS", $"{detail.ServiceKeys} ({detail.ActiveServiceKeys} active)"),
            ("MEMBERS", detail.Members.ToString()));
    }

    private static string Count(int total, string noun) => $"{total} {noun}{(total == 1 ? string.Empty : "s")}";

    private static async Task ListClientsAsync(IAdminService admin)
    {
        var table = new Table("KEY", "NAME", "STATE", "ANON-READ");
        var clients = await admin.ListClientsAsync();

        foreach (var client in clients)
        {
            table.Add(
                client.Key,
                client.Name,
                client.IsActive ? Output.Green("active") : Output.Red("inactive"),
                client.AllowAnonymousContentRead ? "yes" : "no");
        }

        table.Write("No clients.", Count(clients.Count, "client"));
    }

    private static async Task CreateClientAsync(IAdminService admin, CommandOptions options)
    {
        var name = options.Require("name");
        var key = options.Require("key", Slugify(name));
        var client = await admin.CreateClientAsync(key, name, options.GetList("origins"));
        Console.WriteLine($"Created client '{client.Key}' ({client.Id}).");
    }

    private static async Task UpdateClientAsync(IAdminService admin, CommandOptions options)
    {
        var client = await admin.UpdateClientAsync(
            options.Require("key"),
            options.Require("name"),
            options.GetList("origins"),
            options.GetBool("active"),
            options.GetBool("anonymous-read"));

        Console.WriteLine($"Updated client '{client.Key}'.");
    }

    private static async Task ListMembershipsAsync(IAdminService admin, CommandOptions options)
    {
        var clientKey = options.Require("client");
        var table = new Table("EMAIL", "NAME", "STATE", "CAPABILITIES");
        var members = await admin.ListMembershipsAsync(clientKey);

        foreach (var member in members)
        {
            table.Add(
                member.Email,
                member.DisplayName,
                member.IsActive ? Output.Green("active") : Output.Red("inactive"),
                Output.Capabilities(member.Capabilities));
        }

        table.Write($"No memberships on '{clientKey}'.", Count(members.Count, "member"));
    }

    private static async Task SetMembershipAsync(IAdminService admin, CommandOptions options)
    {
        var membership = await admin.UpsertMembershipAsync(options.Require("client"), options.Require("email"), options.GetList("capabilities"));
        Console.WriteLine($"{membership.Email} on '{membership.ClientKey}': {Output.Capabilities(membership.Capabilities)}");
    }

    private static async Task RemoveMembershipAsync(IAdminService admin, CommandOptions options, IInteraction? interaction)
    {
        var clientKey = options.Require("client");
        var email = options.Require("email");

        if (interaction is not null && !interaction.Confirm($"Remove {email} from '{clientKey}'"))
        {
            Output.Note("Cancelled.");
            return;
        }

        await admin.RemoveMembershipAsync(clientKey, email);
        Console.WriteLine($"Removed {email} from '{clientKey}'.");
    }

    private static async Task ListServiceKeysAsync(IAdminService admin, CommandOptions options)
    {
        var clientKey = options.Require("client");
        var now = DateTime.UtcNow;
        var table = new Table("ID", "PREFIX", "NAME", "STATE", "AGE", "LAST USED", "CAPABILITIES");
        var keys = await admin.ListServiceKeysAsync(clientKey);

        foreach (var key in keys)
        {
            var state = key.IsActive(now)
                ? Output.Green("active")
                : key.RevokedAt is not null ? Output.Red("revoked") : Output.Yellow("expired");

            table.Add(
                ShortId(key.Id),
                $"{key.KeyPrefix}...",
                key.Name,
                state,
                Since(key.CreatedAt, now),
                LastUsed(key, now),
                Output.Capabilities(key.Roles));
        }

        var active = keys.Count(key => key.IsActive(now));
        table.Write($"No service keys for '{clientKey}'.", $"{Count(keys.Count, "key")} · {active} active");
    }

    private static string ShortId(Guid id) => id.ToString()[..8];

    private static string LastUsed(ServiceKey key, DateTime now)
    {
        if (key.LastUsedAt is not { } used)
        {
            return key.IsActive(now) ? Output.Yellow("never") : Output.Dim("never");
        }

        var text = Since(used, now);
        return key.IsActive(now) && now - used > TimeSpan.FromDays(StaleDays) ? Output.Yellow(text) : text;
    }

    private static string Since(DateTime moment, DateTime now)
    {
        var elapsed = now - moment;

        return elapsed switch
        {
            { TotalMinutes: < 1 } => "now",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes}m",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours}h",
            { TotalDays: < 30 } => $"{(int)elapsed.TotalDays}d",
            { TotalDays: < 365 } => $"{(int)(elapsed.TotalDays / 30)}mo",
            _ => $"{(int)(elapsed.TotalDays / 365)}y",
        };
    }

    private static async Task CreateServiceKeyAsync(IAdminService admin, CommandOptions options)
    {
        var created = await admin.CreateServiceKeyAsync(
            options.Require("client"),
            options.Require("name"),
            options.RequireList("capabilities"),
            options.GetUtcDateTime("expires"));

        Console.WriteLine(created.RawKey);
        Console.Error.WriteLine($"Created service key {created.Id}; the key above is shown once and cannot be recovered.");
    }

    private static async Task RevokeServiceKeyAsync(IAdminService admin, CommandOptions options, IInteraction? interaction)
    {
        var clientKey = options.Require("client");
        var requested = options.Require("id");

        var matches = (await admin.ListServiceKeysAsync(clientKey))
            .Where(key => key.Id.ToString().StartsWith(requested, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var target = matches.Count switch
        {
            1 => matches[0],
            0 => throw new NotFoundException($"No service key on '{clientKey}' starts with '{requested}'."),
            _ => throw new ValidationException([$"'{requested}' matches {matches.Count} service keys; use more characters."]),
        };

        if (interaction is not null && !interaction.Confirm($"Revoke service key {ShortId(target.Id)} ({target.Name})"))
        {
            Output.Note("Cancelled.");
            return;
        }

        await admin.RevokeServiceKeyAsync(clientKey, target.Id);
        Console.WriteLine($"Revoked service key {ShortId(target.Id)}.");
    }

    private static string? Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            var mapped = TurkishLetters.TryGetValue(character, out var replacement)
                ? replacement
                : char.ToLowerInvariant(character);

            if (char.IsAsciiLetterLower(mapped) || char.IsAsciiDigit(mapped))
            {
                builder.Append(mapped);
            }
            else if (builder.Length > 0 && builder[^1] is not '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxKeyLength)
        {
            slug = slug[..MaxKeyLength].TrimEnd('-');
        }

        return slug.Length == 0 ? null : slug;
    }

    private static void RotateSigningKey(IAdminService admin, IInteraction? interaction)
    {
        if (interaction is not null && !interaction.Confirm("Rotate the signing key"))
        {
            Output.Note("Cancelled.");
            return;
        }

        Console.WriteLine($"Rotated signing key; new kid is {admin.RotateSigningKey()}.");
    }
}
