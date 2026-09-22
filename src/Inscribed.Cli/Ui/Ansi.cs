namespace Inscribed.Cli.Ui;

internal static class Ansi
{
    public const string Reset = "\u001b[0m";

    public const string HideCursor = "\u001b[?25l";

    public const string ShowCursor = "\u001b[?25h";

    public const string ClearBelow = "\u001b[J";

    public const string ClearScreen = "\u001b[2J\u001b[3J\u001b[H";

    public static string To(int row) => $"\u001b[{row + 1};1H";

    public static string Right(int columns) => columns > 0 ? $"\u001b[{columns}C" : string.Empty;
}
