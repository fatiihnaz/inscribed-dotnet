using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Inscribed.Application.Contracts.Repositories;
using Inscribed.Application.Contracts.Services;
using Inscribed.Infrastructure.Cache;
using Inscribed.Infrastructure.Enrichment;
using Inscribed.Infrastructure.Storage;
using Inscribed.Infrastructure.Storage.Repositories;

namespace Inscribed.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<CmsDbContext>(options =>
            options.UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsAssembly(typeof(CmsDbContext).Assembly.FullName)));

        services.AddScoped<IContentBlockRepository, ContentBlockRepository>();
        services.AddScoped<ICollectionItemRepository, CollectionItemRepository>();

        var redisConnectionString = configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");
        services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
        services.AddScoped<IDraftService, RedisDraftService>();
        services.AddScoped<ICollectionDraftService, RedisCollectionDraftService>();

        services.AddCollectionEnrichment(configuration);

        return services;
    }
}