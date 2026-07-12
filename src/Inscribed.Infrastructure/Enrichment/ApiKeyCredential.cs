namespace Inscribed.Infrastructure.Enrichment;

public sealed class ApiKeyCredential : IOutboundCredential
{
    private readonly string _header;
    private readonly string _value;

    public ApiKeyCredential(string name, string header, string value)
    {
        Name = name;
        _header = header;
        _value = value;
    }

    public string Name { get; }

    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(_header);
        request.Headers.TryAddWithoutValidation(_header, _value);
        return ValueTask.CompletedTask;
    }

    public bool Invalidate() => false;
}