namespace Inscribed.Cli.Ui;

internal interface ITerminal
{
    int Width { get; }

    int Height { get; }

    void Write(string text);

    ConsoleKeyInfo ReadKey();

    IDisposable Capture();
}

internal sealed class SystemTerminal : ITerminal
{
    private const int FallbackWidth = 80;

    private const int FallbackHeight = 24;

    private readonly TextWriter _writer;

    public SystemTerminal(TextWriter writer) => _writer = writer;

    public static bool IsInteractive => !Console.IsInputRedirected && !Console.IsErrorRedirected;

    public int Width => Measure(() => Console.WindowWidth, FallbackWidth);

    public int Height => Measure(() => Console.WindowHeight, FallbackHeight);

    public void Write(string text)
    {
        _writer.Write(text);
        _writer.Flush();
    }

    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);

    public IDisposable Capture()
    {
        var treatControlCAsInput = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;

        return new Release(() =>
        {
            Write(Ansi.ShowCursor);
            Console.TreatControlCAsInput = treatControlCAsInput;
        });
    }

    private static int Measure(Func<int> read, int fallback)
    {
        try
        {
            var value = read();
            return value > 0 ? value : fallback;
        }
        catch (Exception exception) when (exception is IOException or PlatformNotSupportedException)
        {
            return fallback;
        }
    }
}

internal sealed class Release : IDisposable
{
    private readonly Action _release;

    public Release(Action release) => _release = release;

    public void Dispose() => _release();
}
