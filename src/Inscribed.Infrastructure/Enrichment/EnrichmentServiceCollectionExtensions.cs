using System.Text.RegularExpressions;
using Inscribed.Application.Contracts.Policies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inscribed.Infrastructure.Enrichment;

public static class EnrichmentServiceCollectionExtensions
{
    private const int DefaultAssumedLifetimeSeconds = 300;

    private static readonly Regex NamePattern = new("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.Compiled);

    public static IServiceCollection AddCollectionEnrichment(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICollectionEnricherFactory, HttpCollectionEnricherFactory>();

        services.AddHttpClient(EnrichmentHttpClients.Anonymous, client => client.Timeout = TimeSpan.FromSeconds(3));
        services.AddHttpClient(EnrichmentHttpClients.Token, client => client.Timeout = TimeSpan.FromSeconds(5));

        var errors = new List<string>();

        foreach (var section in configuration.GetSection("Enrichment:Auth").GetChildren())
        {
            var name = section.Key;
            var sectionRef = $"Enrichment:Auth:{name}";

            if (!NamePattern.IsMatch(name))
            {
                errors.Add($"{sectionRef}: credential names must start with a letter or digit and contain only letters, digits, hyphens, and underscores");
                continue;
            }

            switch (section["Type"])
            {
                case "ApiKey":
                    AddApiKeyCredential(services, section, name, sectionRef, errors);
                    break;

                case "OAuth2ClientCredentials":
                    AddClientCredentials(services, section, name, sectionRef, errors);
                    break;

                case null or "":
                    errors.Add($"{sectionRef}: 'Type' is required ('ApiKey' or 'OAuth2ClientCredentials')");
                    break;

                case { } other:
                    errors.Add($"{sectionRef}: unknown type '{other}' ('ApiKey' or 'OAuth2ClientCredentials')");
                    break;
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Invalid enrichment credential configuration:{Environment.NewLine}" +
                string.Join(Environment.NewLine, errors.Select(e => $"  - {e}")));

        return services;
    }

    private static void AddApiKeyCredential(IServiceCollection services, IConfigurationSection section, string name, string sectionRef, List<string> errors)
    {
        var header = section["Header"];
        var value = section["Value"];

        if (string.IsNullOrWhiteSpace(header))
            errors.Add($"{sectionRef}: 'Header' is required for ApiKey credentials");

        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{sectionRef}: 'Value' is required for ApiKey credentials");

        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(value))
            return;

        services.AddKeyedSingleton<IOutboundCredential>(name, new ApiKeyCredential(name, header, value));
        AddCredentialedClient(services, name);
    }

    private static void AddClientCredentials(IServiceCollection services, IConfigurationSection section, string name, string sectionRef, List<string> errors)
    {
        var tokenEndpoint = section["TokenEndpoint"];
        var clientId = section["ClientId"];
        var clientSecret = section["ClientSecret"];
        var scope = section["Scope"];
        var lifetimeSetting = section["AssumeLifetimeSeconds"];

        if (string.IsNullOrWhiteSpace(tokenEndpoint) || !Uri.TryCreate(tokenEndpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add($"{sectionRef}: 'TokenEndpoint' must be an absolute http(s) URL");

        if (string.IsNullOrWhiteSpace(clientId))
            errors.Add($"{sectionRef}: 'ClientId' is required for OAuth2ClientCredentials credentials");

        if (string.IsNullOrWhiteSpace(clientSecret))
            errors.Add($"{sectionRef}: 'ClientSecret' is required for OAuth2ClientCredentials credentials");

        var assumedLifetime = DefaultAssumedLifetimeSeconds;

        if (lifetimeSetting is not null && (!int.TryParse(lifetimeSetting, out assumedLifetime) || assumedLifetime <= 0))
            errors.Add($"{sectionRef}: 'AssumeLifetimeSeconds' must be a positive integer");

        if (string.IsNullOrWhiteSpace(tokenEndpoint) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return;

        services.AddKeyedSingleton<IOutboundCredential>(name, (sp, _) => new ClientCredentialsTokenProvider(
            name,
            tokenEndpoint,
            clientId,
            clientSecret,
            scope,
            assumedLifetime,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<ClientCredentialsTokenProvider>>()));

        AddCredentialedClient(services, name);
    }

    private static void AddCredentialedClient(IServiceCollection services, string name)
    {
        services.AddHttpClient(EnrichmentHttpClients.For(name), client => client.Timeout = TimeSpan.FromSeconds(3))
            .AddHttpMessageHandler(sp => new OutboundCredentialHandler(sp.GetRequiredKeyedService<IOutboundCredential>(name)));
    }
}