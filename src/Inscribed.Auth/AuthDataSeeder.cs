using Inscribed.Auth.Entities;
using Inscribed.Auth.Options;
using Inscribed.Auth.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth;

public static class AuthDataSeeder
{
    public static IServiceProvider SeedInscribedAuth(this IServiceProvider services)
    {
        var db = services.GetRequiredService<AuthDbContext>();
        var options = services.GetRequiredService<IOptions<AuthOptions>>().Value;

        if (!db.Clients.Any(c => c.Key == options.AdminClientKey))
        {
            db.Clients.Add(Client.Create(options.AdminClientKey, "Admin Console", options.Admin.ConsoleOrigins, DateTime.UtcNow));
            db.SaveChanges();
        }

        return services;
    }
}
