using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Inscribed.Application.Contracts.Policies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Inscribed.Infrastructure.Enrichment;

public sealed class HttpEnricher : ICollectionEnricher
{
    private static readonly Regex PlaceholderPattern = new(@"\{([^{}]*)\}", RegexOptions.Compiled);

    private readonly EnrichmentDefinition _definition;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HttpEnricher> _logger;

    public HttpEnricher(
        EnrichmentDefinition definition,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<HttpEnricher> logger)
    {
        _definition = definition;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default)
    {
        var url = ResolveUrl(slug, data);
        if (url is null)
            return data;

        var response = await FetchAsync(url, cancellationToken);
        if (response is null)
            return data;

        foreach (var (target, path) in _definition.Map)
        {
            if (JsonNodePath.Select(response, path) is { } value)
                data[target] = value.DeepClone();
        }

        return data;
    }

    private string? ResolveUrl(string slug, JsonNode data)
    {
        var missing = false;

        var url = PlaceholderPattern.Replace(_definition.UrlTemplate, match =>
        {
            var placeholder = match.Groups[1].Value;

            if (placeholder == "slug")
                return Uri.EscapeDataString(slug);

            if (data[placeholder] is JsonValue value)
                return Uri.EscapeDataString(value.ToString());

            missing = true;
            return string.Empty;
        });

        return missing ? null : url;
    }

    private async Task<JsonNode?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        var cacheKey = $"enrichment:{_definition.CredentialName}:{url}";

        if (_definition.CacheSeconds > 0 && _cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return JsonNode.Parse(cached);

        try
        {
            var client = _httpClientFactory.CreateClient(EnrichmentHttpClients.For(_definition.CredentialName));
            using var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Enrichment request to {Url} returned {StatusCode}", url, (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var node = JsonNode.Parse(body);

            if (_definition.CacheSeconds > 0)
                _cache.Set(cacheKey, body, TimeSpan.FromSeconds(_definition.CacheSeconds));

            return node;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enrichment request to {Url} failed", url);
            return null;
        }
    }
}