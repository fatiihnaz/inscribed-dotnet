using Inscribed.Auth.Authentication;
using Inscribed.Auth.Options;
using Inscribed.Auth.Services;
using Inscribed.Auth.Storage;
using Inscribed.Auth.Storage.Repositories;
using Microsoft.AspNetCore.Authentication;
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

        services.AddHttpClient();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IGoogleOAuthClient, GoogleOAuthClient>();
        services.AddScoped<IGoogleLoginService, GoogleLoginService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IServiceKeyRepository, ServiceKeyRepository>();
        services.AddScoped<IServiceKeyService, ServiceKeyService>();

        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
        services.AddAuthentication(InscribedAuthSchemes.PolicyScheme)
            .AddPolicyScheme(InscribedAuthSchemes.PolicyScheme, InscribedAuthSchemes.PolicyScheme, options =>
            {
                options.ForwardDefaultSelector = context =>
                    ServiceTokenLocator.Locate(context.Request) is null
                        ? JwtBearerDefaults.AuthenticationScheme
                        : InscribedAuthSchemes.ServiceToken;
            })
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, ServiceTokenAuthenticationHandler>(InscribedAuthSchemes.ServiceToken, null);

        return services;
    }
}