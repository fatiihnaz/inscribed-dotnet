using Inscribed.Auth.Authorization;

namespace Inscribed.Cli;

internal static class Output
{
    private const char EscapeCharacter = '\u001b';
    private const string Reset = "\u001b[0m";

    public static readonly bool Decorated =
        Environment.GetEnvironmentVariable("NO_COLOR") is null
        && (Environment.GetEnvironmentVariable("FORCE_COLOR") is not null || !Console.IsOutputRedirected);

    public static bool Framed { get; set; }

    public static string Indent => Decorated && !Framed ? "  " : string.Empty;

    public static void Note(string message) => Console.Error.WriteLine(Indent + message);

    public static void Blank() => Console.Error.WriteLine();

    public static string Dim(string text) => Paint(text, "\u001b[2m");

    public static string Green(string text) => Paint(text, "\u001b[32m");

    public static string Yellow(string text) => Paint(text, "\u001b[33m");

    public static string Red(string text) => Paint(text, "\u001b[31m");

    public static string Bold(string text) => Paint(text, "\u001b[1m");

    public static string Accent(string text) => Paint(text, AccentCode);

    private static readonly string AccentCode = TrueColor()
        ? "\u001b[38;2;224;200;229m"
        : "\u001b[38;5;182m";

    private static bool TrueColor() =>
        OperatingSystem.IsWindows()
        || Environment.GetEnvironmentVariable("COLORTERM") is "truecolor" or "24bit"
        || Environment.GetEnvironmentVariable("TERM_PROGRAM") is "vscode";

    public static string Capability(string capability) => capability switch
    {
        CapabilityCatalog.ServiceAdmin or CapabilityCatalog.ClientAdmin => Red(capability),
        CapabilityCatalog.ContentWrite or CapabilityCatalog.SchemaSync => Yellow(capability),
        _ => capability,
    };

    public static string Capabilities(IReadOnlyList<string> capabilities) =>
        capabilities.Count == 0 ? Dim("(none)") : string.Join(" ", capabilities.Select(Capability));

    public static string Describe(Exception exception)
    {
        var innermost = exception;

        while (innermost.InnerException is { } inner)
        {
            innermost = inner;
        }

        return ReferenceEquals(innermost, exception) ? exception.Message : $"{exception.Message} ({innermost.Message})";
    }

    public static void Detail(params (string Label, string Value)[] rows)
    {
        var width = rows.Max(row => row.Label.Length);

        if (Decorated)
        {
            Console.WriteLine();
        }

        foreach (var (label, value) in rows)
        {
            Console.WriteLine($"{Indent}{Dim(label.PadRight(width))}  {value}");
        }

        if (Decorated)
        {
            Console.WriteLine();
        }
    }

    private static string Paint(string text, string code) =>
        Decorated && text.Length > 0 ? code + text + Reset : text;

    public static int VisibleLength(string value)
    {
        var length = 0;
        var escaped = false;

        foreach (var character in value)
        {
            if (escaped)
            {
                escaped = character is not 'm';
                continue;
            }

            if (character == EscapeCharacter)
            {
                escaped = true;
                continue;
            }

            length++;
        }

        return length;
    }
}

internal sealed class Table
{
    private readonly string[] _headers;
    private readonly List<string[]> _rows = [];

    public Table(params string[] headers) => _headers = headers;

    public void Add(params string[] cells) => _rows.Add(cells);

    public void Write(string emptyMessage, string? summary = null)
    {
        if (_rows.Count == 0)
        {
            Output.Blank();
            Output.Note(Output.Dim(emptyMessage));
            Output.Blank();
            return;
        }

        var widths = new int[_headers.Length];
        for (var column = 0; column < _headers.Length; column++)
        {
            widths[column] = _headers[column].Length;
            foreach (var row in _rows)
            {
                widths[column] = Math.Max(widths[column], Output.VisibleLength(row[column]));
            }
        }

        if (Output.Decorated)
        {
            Console.WriteLine();
            Console.WriteLine(Output.Indent + Output.Dim(Compose(_headers, widths)));
            Console.WriteLine(Output.Indent + Output.Dim(Compose([.. widths.Select(width => new string('─', width))], widths)));
        }

        foreach (var row in _rows)
        {
            Console.WriteLine(Output.Indent + Compose(row, widths));
        }

        if (Output.Decorated)
        {
            if (summary is not null)
            {
                Console.WriteLine();
                Console.WriteLine(Output.Indent + Output.Dim(summary));
            }

            Console.WriteLine();
        }

        Console.Out.Flush();
    }

    private static string Compose(string[] cells, int[] widths) =>
        string.Join("  ", cells.Select((cell, column) => Pad(cell, widths[column]))).TrimEnd();

    private static string Pad(string value, int width) =>
        value + new string(' ', Math.Max(0, width - Output.VisibleLength(value)));
}
