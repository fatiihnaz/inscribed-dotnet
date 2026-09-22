using System.Text;

namespace Inscribed.Cli.Ui;

internal static class Wordmark
{
    private const string Shade = "╗╔╝╚═║";

    public static readonly string[] Large =
    [
        "██╗███╗   ██╗███████╗ ██████╗██████╗ ██╗██████╗ ███████╗██████╗ ",
        "██║████╗  ██║██╔════╝██╔════╝██╔══██╗██║██╔══██╗██╔════╝██╔══██╗",
        "██║██╔██╗ ██║███████╗██║     ██████╔╝██║██████╔╝█████╗  ██║  ██║",
        "██║██║╚██╗██║╚════██║██║     ██╔══██╗██║██╔══██╗██╔══╝  ██║  ██║",
        "██║██║ ╚████║███████║╚██████╗██║  ██║██║██████╔╝███████╗██████╔╝",
        "╚═╝╚═╝  ╚═══╝╚══════╝ ╚═════╝╚═╝  ╚═╝╚═╝╚═════╝ ╚══════╝╚═════╝ ",
    ];

    public static readonly string[] Small =
    [
        "╦┌┐┌┌─┐┌─┐┬─┐┬┌┐ ┌─┐┌┬┐",
        "║│││└─┐│  ├┬┘│├┴┐├┤  ││",
        "╩┘└┘└─┘└─┘┴└─┴└─┘└─┘─┴┘",
    ];

    public static IReadOnlyList<string> Render(int available) =>
        available >= Large[0].Length ? [.. Large.Select(Shaded)]
        : available >= Small[0].Length ? [.. Small.Select(row => Output.Accent(row.TrimEnd()))]
        : [];

    private static string Shaded(string row)
    {
        var builder = new StringBuilder();
        var index = 0;

        while (index < row.Length)
        {
            var start = index;
            var kind = Kind(row[index]);

            while (index < row.Length && Kind(row[index]) == kind)
            {
                index++;
            }

            var run = row[start..index];
            builder.Append(kind switch
            {
                'b' => Output.Accent(run),
                's' => Output.Dim(Output.Accent(run)),
                _ => run,
            });
        }

        return builder.ToString().TrimEnd();
    }

    private static char Kind(char character) =>
        character == ' ' ? ' ' : Shade.Contains(character) ? 's' : 'b';
}
