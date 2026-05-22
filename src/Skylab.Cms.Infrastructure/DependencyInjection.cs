using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Skylab.Cms.Application.Contracts.Repositories;
using Skylab.Cms.Application.Contracts.Services;
using Skylab.Cms.Infrastructure.Cache;
using Skylab.Cms.Infrastructure.Storage;
using Skylab.Cms.Infrastructure.Storage.Repositories;

namespace Skylab.Cms.Infrastructure;

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

        return services;
    }
}