using Inscribed.Application.Contracts.Identity;
using Inscribed.Auth.Authentication;
using Inscribed.Auth.Authorization;
using Inscribed.Auth.Endpoints;
using Inscribed.Auth.Identity;
using Inscribed.Auth.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Inscribed.Auth;

public static class DependencyInjection
{
    public static AuthMode ReadAuthMode(this IConfiguration configuration)
        => configuration.GetValue("Auth:Mode", AuthMode.BuiltIn);

    public static IServiceCollection AddInscribedAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection("Auth"))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();

        services.AddSingleton<IAdministratorPolicy, CapabilityAdministratorPolicy>();
        services.AddSingleton<IPrincipalTenant, ClaimPrincipalTenant>();
        services.TryAddScoped<IClientIdentityStore, NullClientIdentityStore>();

        services.AddSingleton<IConfigureOptions<JwtBearerOptions>>(provider =>
            new ConfigureJwtBearerOptions(
                provider.GetRequiredService<IOptions<AuthOptions>>(),
                provider.GetService<ITokenValidationKeySource>()));

        services.AddAuthentication(InscribedAuthSchemes.PolicyScheme)
            .AddPolicyScheme(InscribedAuthSchemes.PolicyScheme, InscribedAuthSchemes.PolicyScheme, options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    foreach (var selector in context.RequestServices.GetServices<IAuthenticationSchemeSelector>())
                    {
                        if (selector.Select(context.Request) is { } scheme)
                        {
                            return scheme;
                        }
                    }

                    return JwtBearerDefaults.AuthenticationScheme;
                };
            })
            .AddJwtBearer();

        return services;
    }

    public static IEndpointRouteBuilder MapInscribedAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapWhoAmIEndpoint();

        foreach (var module in app.ServiceProvider.GetServices<IAuthIssuerModule>())
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
