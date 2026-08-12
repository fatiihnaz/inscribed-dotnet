using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Inscribed.Application.Contracts.Identity;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Services;
using Inscribed.Application.Services.Policies;

namespace Inscribed.Application;

public static class DependencyInjection
{
    private const string DefaultCollectionsPath = "collections";

    public static IServiceCollection AddClientManagement(this IServiceCollection services)
    {
        services.AddScoped<IClientService, ClientService>();
        return services;
    }

    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddClientManagement();
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<ICollectionService, CollectionService>();

        var credentialNames = configuration.GetSection("Enrichment:Auth").GetChildren()
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var configuredPath = configuration["Collections:Path"];
        var definitions = FileCollectionPolicyLoader.Load(configuredPath ?? DefaultCollectionsPath, required: configuredPath is not null, credentialNames);

        foreach (var definition in definitions)
        {
            services.AddSingleton<ICollectionPolicy>(sp =>
            {
                var administrators = sp.GetRequiredService<IAdministratorPolicy>();

                if (definition.Enrichments.Count == 0)
                    return new FileCollectionPolicy(definition, [], administrators);

                var factory = sp.GetRequiredService<ICollectionEnricherFactory>();
                return new FileCollectionPolicy(definition, definition.Enrichments.Select(factory.Create).ToArray(), administrators);
            });
        }

        services.AddSingleton<ICollectionPolicyResolver, CollectionPolicyResolver>();

        return services;
    }
}
