using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Inscribed.Infrastructure.Enrichment;

public sealed class ClientCredentialsTokenProvider : IOutboundCredential
{
    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string? _scope;
    private readonly int _assumeLifetimeSeconds;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClientCredentialsTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _refreshAfter = DateTimeOffset.MinValue;

    public ClientCredentialsTokenProvider(
        string name,
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        string? scope,
        int assumeLifetimeSeconds,
        IHttpClientFactory httpClientFactory,
        ILogger<ClientCredentialsTokenProvider> logger)
    {
        Name = name;
        _tokenEndpoint = tokenEndpoint;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _scope = scope;
        _assumeLifetimeSeconds = assumeLifetimeSeconds;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name { get; }

    public async ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
    }

    public bool Invalidate()
    {
        _refreshAfter = DateTimeOffset.MinValue;
        return true;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is { } token && DateTimeOffset.UtcNow < _refreshAfter)
            return token;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is { } cached && DateTimeOffset.UtcNow < _refreshAfter)
                return cached;

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
            };

            if (_scope is not null)
                form["scope"] = _scope;

            var client = _httpClientFactory.CreateClient(EnrichmentHttpClients.Token);
            using var response = await client.PostAsync(_tokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token endpoint for credential '{Credential}' returned {StatusCode}", Name, (int)response.StatusCode);
                throw new HttpRequestException($"Token endpoint for credential '{Name}' returned {(int)response.StatusCode}.");
            }

            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            var accessToken = body?["access_token"] is JsonValue tokenValue && tokenValue.TryGetValue<string>(out var text)
                ? text
                : throw new HttpRequestException($"Token endpoint for credential '{Name}' returned no access_token.");

            var lifetime = ReadLifetimeSeconds(body);
            _accessToken = accessToken;
            _refreshAfter = DateTimeOffset.UtcNow.AddSeconds(Math.Max(lifetime - 60, lifetime / 2.0));

            return accessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private int ReadLifetimeSeconds(JsonNode? body)
    {
        if (body?["expires_in"] is not JsonValue value)
            return _assumeLifetimeSeconds;

        if (value.TryGetValue<int>(out var seconds))
            return seconds > 0 ? seconds : _assumeLifetimeSeconds;

        if (value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed))
            return parsed > 0 ? parsed : _assumeLifetimeSeconds;

        return _assumeLifetimeSeconds;
    }
}