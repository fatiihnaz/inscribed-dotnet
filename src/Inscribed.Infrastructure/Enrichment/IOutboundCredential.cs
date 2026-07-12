namespace Inscribed.Infrastructure.Enrichment;

public interface IOutboundCredential
{
    string Name { get; }

    ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);

    bool Invalidate();
}