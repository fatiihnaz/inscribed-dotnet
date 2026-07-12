using Inscribed.Application.Contracts.Policies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Inscribed.Infrastructure.Enrichment;

public sealed class HttpCollectionEnricherFactory : ICollectionEnricherFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HttpEnricher> _logger;

    public HttpCollectionEnricherFactory(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<HttpEnricher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public ICollectionEnricher Create(EnrichmentDefinition definition)
        => new HttpEnricher(definition, _httpClientFactory, _cache, _logger);
}