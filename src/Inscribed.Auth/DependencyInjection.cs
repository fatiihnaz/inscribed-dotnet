using Inscribed.Auth.Authentication;
using Inscribed.Auth.Options;
using Inscribed.Auth.Services;
using Inscribed.Auth.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth;

public static class DependencyInjection
{
    public static IServiceCollection AddInscribedAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection("Auth"))
            .Validate<IHostEnvironment>(
                (options, env) => !env.IsProduction()
                    || (!string.IsNullOrWhiteSpace(options.Issuer)
                        && !options.Issuer.Contains("localhost", StringComparison.OrdinalIgnoreCase)),
                "Auth:Issuer must be set to the public URL in Production.")
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AuthDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AuthDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__ef_migrations_history_auth");
            }));

        services.AddSingleton<ISigningKeyStore, SigningKeyStore>();
        services.AddSingleton<IJwtIssuer, JwtIssuer>();

        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        return services;
    }
}
