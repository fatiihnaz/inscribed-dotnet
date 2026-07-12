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

        var configuredPath = configuration["Collections:Path"];
        var policies = FileCollectionPolicyLoader.Load(configuredPath ?? DefaultCollectionsPath, required: configuredPath is not null);

        foreach (var policy in policies)
            services.AddSingleton<ICollectionPolicy>(policy);

        services.AddSingleton<ICollectionPolicyResolver, CollectionPolicyResolver>();

        return services;
    }
}
