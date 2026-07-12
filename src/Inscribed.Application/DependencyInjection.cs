using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Services;
using Inscribed.Application.Services.Policies;

namespace Inscribed.Application;

public static class DependencyInjection
{
    private const string DefaultCollectionsPath = "collections";

    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<ICollectionService, CollectionService>();

        var credentialNames = configuration.GetSection("Enrichment:Auth").GetChildren()
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var configuredPath = configuration["Collections:Path"];
        var definitions = FileCollectionPolicyLoader.Load(configuredPath ?? DefaultCollectionsPath, required: configuredPath is not null, credentialNames);

        foreach (var definition in definitions)
        {
            if (definition.Enrichments.Count == 0)
            {
                services.AddSingleton<ICollectionPolicy>(new FileCollectionPolicy(definition, []));
                continue;
            }

            services.AddSingleton<ICollectionPolicy>(sp =>
            {
                var factory = sp.GetRequiredService<ICollectionEnricherFactory>();
                return new FileCollectionPolicy(definition, definition.Enrichments.Select(factory.Create).ToArray());
            });
        }

        services.AddSingleton<ICollectionPolicyResolver, CollectionPolicyResolver>();

        return services;
    }
}
