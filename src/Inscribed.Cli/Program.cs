using Inscribed.Application;
using Inscribed.Auth;
using Inscribed.Auth.Issuer;
using Inscribed.Auth.Options;
using Inscribed.Cli;
using Inscribed.Infrastructure;
using Inscribed.Domain.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Net.Sockets;
using System.Text;

var encoding = UseUtf8();

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
    Console.Error.WriteLine(Output.Describe(exception));

    if (Unreachable(exception))
    {
        Console.Error.WriteLine("Nothing is listening there. 'docker compose up -d db' starts the packaged database.");
    }

    return 1;
}
finally
{
    if (encoding is not null)
    {
        Console.OutputEncoding = encoding;
    }
}

static bool Unreachable(Exception exception)
{
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (current is SocketException)
        {
            return true;
        }
    }

    return false;
}

static Encoding? UseUtf8()
{
    try
    {
        var previous = Console.OutputEncoding;
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return previous;
    }
    catch (IOException)
    {
        return null;
    }
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

    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default"))
        && EnvFile.Read(Directory.GetCurrentDirectory()) is { } settings)
    {
        builder.Configuration.AddInMemoryCollection(settings);
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default")))
    {
        throw new InvalidOperationException(
            "No database configured. Run from a directory whose .env carries DB_PASSWORD (the repository root does), "
            + "or set ConnectionStrings__Default yourself.");
    }

    builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions()));
    builder.Services.AddInfrastructureStorage(builder.Configuration);
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInscribedAuth(builder.Configuration);

    if (builder.Configuration.ReadAuthMode() is AuthMode.BuiltIn)
    {
        builder.Services.AddInscribedAuthIssuer(builder.Configuration);
    }

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
