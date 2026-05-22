using Microsoft.Extensions.DependencyInjection;
using Skylab.Cms.Application.Contracts.Policies;
using Skylab.Cms.Application.Services;
using Skylab.Cms.Application.Services.Policies;

namespace Skylab.Cms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<ICollectionService, CollectionService>();

        services.AddSingleton<ICollectionPolicy, TeamsCollectionPolicy>();
        services.AddSingleton<ICollectionPolicy, NewsCollectionPolicy>();
        services.AddSingleton<ICollectionPolicyResolver, CollectionPolicyResolver>();

        return services;
    }
}