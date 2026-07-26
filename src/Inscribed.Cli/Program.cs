using Inscribed.Auth;
using Inscribed.Auth.Services;
using Inscribed.Cli;
using Inscribed.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

try
{
    return await RunAsync(args);
}
catch (UsageException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(AdminCommands.Usage);
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
    {
        Console.WriteLine(AdminCommands.Usage);
        return 0;
    }

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions()));
    builder.Services.AddInscribedAuth(builder.Configuration);

    using var host = builder.Build();
    using var scope = host.Services.CreateScope();

    await AdminCommands.RunAsync(scope.ServiceProvider.GetRequiredService<IAdminService>(), args);
    return 0;
}
