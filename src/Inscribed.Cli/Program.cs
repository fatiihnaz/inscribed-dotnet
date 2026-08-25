using Inscribed.Application;
using Inscribed.Auth;
using Inscribed.Auth.Issuer;
using Inscribed.Cli;
using Inscribed.Infrastructure;
using Inscribed.Domain.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

try
{
    return await RunAsync(args);
}
catch (UsageException exception)
{
    Console.Error.WriteLine(exception.Message);
    AdminCommands.WriteHelp(interactive: false);
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    var interactive = args.Length == 0;

    if (!interactive && args[0] is "help" or "--help" or "-h")
    {
        AdminCommands.WriteHelp(interactive: false);
        return 0;
    }

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions()));
    builder.Services.AddInfrastructureStorage(builder.Configuration);
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInscribedAuth(builder.Configuration);
    builder.Services.AddInscribedAuthIssuer(builder.Configuration);

    using var host = builder.Build();

    if (interactive)
    {
        await InteractiveShell.RunAsync(
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            Describe(builder.Configuration.GetConnectionString("Default")));
        return 0;
    }

    using var scope = host.Services.CreateScope();
    await AdminCommands.RunAsync(scope.ServiceProvider, args);
    return 0;
}

static string Describe(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return "no connection string";
    }

    try
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{settings.Host}:{settings.Port}/{settings.Database}";
    }
    catch (ArgumentException)
    {
        return "unparsable connection string";
    }
}
