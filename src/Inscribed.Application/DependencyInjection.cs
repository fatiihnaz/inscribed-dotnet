using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            .Select(section => section.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var configuredPath = configuration["Collections:Path"];

        services.AddSingleton(new EnrichmentCredentialNames(credentialNames));
        services.AddSingleton(new CollectionsPath(configuredPath ?? DefaultCollectionsPath, Required: configuredPath is not null));

        services.AddScoped<ICollectionDefinitionAdminService, CollectionDefinitionAdminService>();
        services.AddScoped<CollectionSeeder>();

        services.AddSingleton<CollectionPolicyRegistry>();
        services.AddSingleton<ICollectionPolicyResolver>(sp => sp.GetRequiredService<CollectionPolicyRegistry>());

        return services;
    }
}
