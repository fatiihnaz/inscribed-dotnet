using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StackExchange.Redis;

namespace Inscribed.Infrastructure.Cache;

public sealed class RedisKeyScanner : IAsyncDisposable
{
    private static readonly Regex GlobSpecials = new(@"[\\*?\[\]]");

    private readonly Lazy<Task<ConnectionMultiplexer>> _connection;

    public RedisKeyScanner(string configuration)
    {
        _connection = new(() => ConnectionMultiplexer.ConnectAsync(configuration, options => options.AbortOnConnectFail = false));
    }

    public static string Escape(string literal) => GlobSpecials.Replace(literal, @"\$0");

    public async IAsyncEnumerable<string> ScanAsync(string pattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var connection = await _connection.Value;

        foreach (var server in connection.GetServers().Where(server => !server.IsReplica))
        {
            await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
                yield return key.ToString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
            await _connection.Value.Result.DisposeAsync();
    }
}
