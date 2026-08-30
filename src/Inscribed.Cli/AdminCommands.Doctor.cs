using Inscribed.Application.Services;
using Inscribed.Application.Services.Policies;
using Inscribed.Auth.Options;
using Inscribed.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inscribed.Cli;

internal static partial class AdminCommands
{
    private static async Task RunDoctorAsync(IServiceProvider services)
    {
        var auth = services.GetRequiredService<IOptions<AuthOptions>>().Value;

        var rows = new List<(string, string)>
        {
            ("DATABASE", await ProbeDatabaseAsync(services)),
            ("AUTH MODE", auth.Mode.ToString()),
        };

        if (auth.Mode is AuthMode.External)
        {
            rows.Add(("AUTHORITY", string.IsNullOrWhiteSpace(auth.Authority) ? Output.Red("(not set)") : auth.Authority));
            rows.Add(("DISCOVERY", await ProbeDiscoveryAsync(auth.Authority)));
        }
        else
        {
            rows.Add(("ISSUER", auth.Issuer));
        }

        rows.Add(("TENANT CLAIM", auth.TenantClaim));
        rows.Add(("ROLES CLAIM", auth.RolesClaim));
        rows.Add(("CLIENTS", await ProbeClientsAsync(services)));

        Output.Detail([.. rows]);
        await WriteClaimRequirementsAsync(services, auth);
    }

    private static async Task<string> ProbeDatabaseAsync(IServiceProvider services)
    {
        try
        {
            var database = services.GetRequiredService<CmsDbContext>().Database;

            if (!await database.CanConnectAsync())
            {
                return Output.Red("unreachable");
            }

            var pending = (await database.GetPendingMigrationsAsync()).ToList();

            return pending.Count == 0
                ? Output.Green("reachable, schema up to date")
                : Output.Red($"reachable, {pending.Count} pending migration(s): {string.Join(", ", pending)}");
        }
        catch (Exception exception)
        {
            return Output.Red(exception.Message);
        }
    }

    private static async Task<string> ProbeDiscoveryAsync(string authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return Output.Red("skipped, no authority configured");
        }

        var url = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync(url);

            return response.IsSuccessStatusCode
                ? Output.Green($"reachable ({(int)response.StatusCode})")
                : Output.Red($"{url} answered {(int)response.StatusCode}");
        }
        catch (Exception exception)
        {
            return Output.Red($"{url}: {exception.Message}");
        }
    }

    private static async Task<string> ProbeClientsAsync(IServiceProvider services)
    {
        try
        {
            var clients = await services.GetRequiredService<IClientService>().ListAsync();

            return clients.Count == 0
                ? Output.Red("none registered; a token's tenant claim will never match")
                : string.Join(", ", clients.Select(client => client.Key));
        }
        catch (Exception exception)
        {
            return Output.Red(exception.Message);
        }
    }

    private static async Task WriteClaimRequirementsAsync(IServiceProvider services, AuthOptions auth)
    {
        var required = new SortedSet<string>(StringComparer.Ordinal)
        {
            "sub",
            auth.TenantClaim,
            auth.RolesClaim,
            "name",
            "email",
        };

        var perCollection = new List<(string Key, string[] Claims)>();

        try
        {
            var definitions = services.GetRequiredService<ICollectionDefinitionAdminService>();

            foreach (var stored in await definitions.ListAsync())
            {
                if (definitions.Validate(stored.Document, $"db:{stored.Key}").Definition is not { } definition)
                {
                    continue;
                }

                var claims = CollectionClaims.Required(definition);

                if (claims.Length > 0)
                {
                    perCollection.Add((stored.Key, claims));
                    required.UnionWith(claims);
                }
            }
        }
        catch (Exception exception)
        {
            Output.Blank();
            Output.Note(Output.Red($"Could not read collection definitions: {exception.Message}"));
            return;
        }

        Output.Blank();
        Output.Note(Output.Bold("CLAIMS YOUR ISSUER MUST EMIT"));
        Output.Note("  " + string.Join("  ", required));

        if (perCollection.Count > 0)
        {
            Output.Blank();
            Output.Note(Output.Dim("Beyond the core five, these come from collection definitions:"));

            foreach (var (key, claims) in perCollection.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                Output.Note($"  {key}{Output.Dim(" -> ")}{string.Join(", ", claims)}");
            }
        }

        Output.Blank();
        Output.Note(Output.Dim("'email' is required on human principals only; machine credentials must not carry it."));
    }
}
