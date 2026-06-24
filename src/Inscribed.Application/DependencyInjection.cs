using Microsoft.Extensions.DependencyInjection;
using Inscribed.Application.Contracts.Policies;
using Inscribed.Application.Services;
using Inscribed.Application.Services.Policies;

namespace Inscribed.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<ICollectionService, CollectionService>();

        services.AddSingleton<ICollectionPolicy, NewsCollectionPolicy>();
        services.AddSingleton<ICollectionPolicyResolver, CollectionPolicyResolver>();

        return services;
    }
}