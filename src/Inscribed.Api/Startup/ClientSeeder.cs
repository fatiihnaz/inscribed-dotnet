using Inscribed.Application.Contracts.Repositories;
using Inscribed.Auth.Options;
using Inscribed.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Inscribed.Api.Startup;

public static class ClientSeeder
{
    public static IServiceProvider SeedInscribedClients(this IServiceProvider services)
    {
        var clients = services.GetRequiredService<IClientRepository>();
        var adminClientKey = services.GetRequiredService<IOptions<AuthOptions>>().Value.AdminClientKey;

        if (clients.GetByKeyAsync(adminClientKey).GetAwaiter().GetResult() is not null)
        {
            return services;
        }

        clients.AddAsync(Client.Create(adminClientKey, DateTime.UtcNow)).GetAwaiter().GetResult();
        clients.SaveChangesAsync().GetAwaiter().GetResult();

        return services;
    }
}
