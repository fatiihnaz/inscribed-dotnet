using Inscribed.Auth.Services;

namespace Inscribed.Cli;

internal static class AdminCommands
{
    public const string Usage = """
        Inscribed admin console.

        Usage:
          user list
          client list
          client create      --key <key> --name <name> [--origins <a,b>]
          client update      --key <key> --name <name> [--origins <a,b>] [--active <bool>] [--anonymous-read <bool>]
          membership set     --client <key> --email <email> [--roles <a,b>]
          membership remove  --client <key> --email <email>
          service-key list   --client <key>
          service-key create --client <key> --name <name> [--roles <a,b>] [--expires <date>]
          service-key revoke --client <key> --id <guid>
          signing-key rotate

        Roles: cms:read, cms:access, cms:admin.
        Set ConnectionStrings__Default to the Inscribed database before running.
        """;

    public static async Task RunAsync(IAdminService admin, string[] args)
    {
        var group = args[0];
        var action = args.Length > 1 ? args[1] : string.Empty;
        var options = CommandOptions.Parse(args, 2);

        switch (group, action)
        {
            case ("user", "list"):
                await ListUsersAsync(admin);
                return;
            case ("client", "list"):
                await ListClientsAsync(admin);
                return;
            case ("client", "create"):
                await CreateClientAsync(admin, options);
                return;
            case ("client", "update"):
                await UpdateClientAsync(admin, options);
                return;
            case ("membership", "set"):
                await SetMembershipAsync(admin, options);
                return;
            case ("membership", "remove"):
                await RemoveMembershipAsync(admin, options);
                return;
            case ("service-key", "list"):
                await ListServiceKeysAsync(admin, options);
                return;
            case ("service-key", "create"):
                await CreateServiceKeyAsync(admin, options);
                return;
            case ("service-key", "revoke"):
                await RevokeServiceKeyAsync(admin, options);
                return;
            case ("signing-key", "rotate"):
                RotateSigningKey(admin);
                return;
            default:
                throw new UsageException($"Unknown command '{string.Join(' ', args.Take(2))}'.");
        }
    }

    private static async Task ListUsersAsync(IAdminService admin)
    {
        foreach (var user in await admin.ListUsersAsync())
        {
            var google = user.GoogleSubject is null ? "unlinked" : "linked";
            var state = user.IsActive ? "active" : "inactive";
            Console.WriteLine($"{user.Email,-36} {user.DisplayName,-24} {google,-9} {state}");
        }
    }

    private static async Task ListClientsAsync(IAdminService admin)
    {
        foreach (var client in await admin.ListClientsAsync())
        {
            var state = client.IsActive ? "active" : "inactive";
            Console.WriteLine($"{client.Key,-24} {client.Name,-28} {state,-9} anonymous-read={client.AllowAnonymousContentRead}");
        }
    }

    private static async Task CreateClientAsync(IAdminService admin, CommandOptions options)
    {
        var client = await admin.CreateClientAsync(options.Require("key"), options.Require("name"), options.GetList("origins"));
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

    private static async Task SetMembershipAsync(IAdminService admin, CommandOptions options)
    {
        var membership = await admin.UpsertMembershipAsync(options.Require("client"), options.Require("email"), options.GetList("roles"));
        var roles = membership.Roles.Length == 0 ? "(none)" : string.Join(", ", membership.Roles);
        Console.WriteLine($"{membership.Email} on '{membership.ClientKey}': {roles}");
    }

    private static async Task RemoveMembershipAsync(IAdminService admin, CommandOptions options)
    {
        var clientKey = options.Require("client");
        var email = options.Require("email");
        await admin.RemoveMembershipAsync(clientKey, email);
        Console.WriteLine($"Removed {email} from '{clientKey}'.");
    }

    private static async Task ListServiceKeysAsync(IAdminService admin, CommandOptions options)
    {
        var now = DateTime.UtcNow;
        foreach (var key in await admin.ListServiceKeysAsync(options.Require("client")))
        {
            var state = key.IsActive(now) ? "active" : key.RevokedAt is not null ? "revoked" : "expired";
            var roles = key.Roles.Length == 0 ? "(none)" : string.Join(",", key.Roles);
            Console.WriteLine($"{key.Id}  {key.KeyPrefix}...  {key.Name,-24} {state,-8} {roles}");
        }
    }

    private static async Task CreateServiceKeyAsync(IAdminService admin, CommandOptions options)
    {
        var created = await admin.CreateServiceKeyAsync(
            options.Require("client"),
            options.Require("name"),
            options.GetList("roles"),
            options.GetUtcDateTime("expires"));

        Console.WriteLine(created.RawKey);
        Console.Error.WriteLine($"Created service key {created.Id}; the key above is shown once and cannot be recovered.");
    }

    private static async Task RevokeServiceKeyAsync(IAdminService admin, CommandOptions options)
    {
        var id = options.RequireGuid("id");
        await admin.RevokeServiceKeyAsync(options.Require("client"), id);
        Console.WriteLine($"Revoked service key {id}.");
    }

    private static void RotateSigningKey(IAdminService admin)
    {
        Console.WriteLine($"Rotated signing key; new kid is {admin.RotateSigningKey()}.");
    }
}
