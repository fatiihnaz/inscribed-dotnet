namespace Inscribed.Infrastructure.Enrichment;

public static class EnrichmentHttpClients
{
    public const string Anonymous = "enrichment";
    public const string Token = "enrichment-token";

    public static string For(string? credentialName)
        => credentialName is null ? Anonymous : $"enrichment:{credentialName}";
}