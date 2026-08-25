using Inscribed.Application.Contracts.Repositories;
using Inscribed.Domain.Entities;

namespace Inscribed.Api.Startup;

public static class ClientSeeder
{
    private const string DefaultAdminClientKey = "admin";

    public static IServiceProvider SeedInscribedClients(this IServiceProvider services)
    {
        var clients = services.GetRequiredService<IClientRepository>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var adminClientKey = configuration["Auth:AdminClientKey"] ?? DefaultAdminClientKey;

        if (clients.GetByKeyAsync(adminClientKey).GetAwaiter().GetResult() is not null)
        {
            return services;
        }

        clients.AddAsync(Client.Create(adminClientKey, DateTime.UtcNow)).GetAwaiter().GetResult();
        clients.SaveChangesAsync().GetAwaiter().GetResult();

        return services;
    }
}
