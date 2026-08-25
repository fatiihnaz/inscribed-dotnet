using Inscribed.Auth.Issuer.Entities;
using Inscribed.Auth.Issuer.Options;
using Inscribed.Auth.Issuer.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth.Issuer;

public static class AuthDataSeeder
{
    public static IServiceProvider SeedInscribedAuth(this IServiceProvider services)
    {
        var db = services.GetRequiredService<AuthDbContext>();
        var options = services.GetRequiredService<IOptions<AuthIssuerOptions>>().Value;

        if (!db.Clients.Any(c => c.Key == options.AdminClientKey))
        {
            db.Clients.Add(ClientIdentity.Create(options.AdminClientKey, "Admin Console", options.Admin.ConsoleOrigins, DateTime.UtcNow));
            db.SaveChanges();
        }

        return services;
    }
}
