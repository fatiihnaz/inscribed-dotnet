using System.Net;

namespace Inscribed.Infrastructure.Enrichment;

public sealed class OutboundCredentialHandler : DelegatingHandler
{
    private readonly IOutboundCredential _credential;

    public OutboundCredentialHandler(IOutboundCredential credential)
    {
        _credential = credential;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _credential.ApplyAsync(request, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || request.Content is not null || !_credential.Invalidate())
            return response;

        response.Dispose();

        using var retry = Clone(request);
        await _credential.ApplyAsync(retry, cancellationToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        foreach (var (key, values) in request.Headers)
            clone.Headers.TryAddWithoutValidation(key, values);

        return clone;
    }
}