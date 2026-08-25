using Inscribed.Application.Contracts.Identity;
using Inscribed.Auth.Authentication;
using Inscribed.Auth.Issuer.Authentication;
using Inscribed.Auth.Issuer.Options;
using Inscribed.Auth.Issuer.Services;
using Inscribed.Auth.Issuer.Storage;
using Inscribed.Auth.Issuer.Storage.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inscribed.Auth.Issuer;

public static class DependencyInjection
{
    public static IServiceCollection AddInscribedAuthIssuer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthIssuerOptions>()
            .Bind(configuration.GetSection("Auth"));

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AuthDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AuthDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__ef_migrations_history_auth");
            }));

        services.AddSingleton<ISigningKeyStore, SigningKeyStore>();
        services.AddSingleton<ITokenValidationKeySource>(provider => provider.GetRequiredService<ISigningKeyStore>());
        services.AddSingleton<IJwtIssuer, JwtIssuer>();

        services.AddHttpClient();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IClientIdentityRepository, ClientIdentityRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IGoogleOAuthClient, GoogleOAuthClient>();
        services.AddScoped<IGoogleLoginService, GoogleLoginService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IServiceKeyRepository, ServiceKeyRepository>();
        services.AddScoped<IServiceKeyService, ServiceKeyService>();
        services.AddScoped<IAdminService, AdminService>();

        services.Replace(ServiceDescriptor.Scoped<IClientIdentityStore, AuthClientIdentityStore>());

        services.AddSingleton<IAuthenticationSchemeSelector, ServiceTokenSchemeSelector>();
        services.AddSingleton<IAuthIssuerModule, InscribedAuthIssuerModule>();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ServiceTokenAuthenticationHandler>(InscribedIssuerSchemes.ServiceToken, null);

        return services;
    }
}
