namespace Inscribed.Cli.Ui;

internal static class Box
{
    public static List<string> Draw(IEnumerable<string> content, int width, Func<string, string> border)
    {
        width = Math.Max(4, width);

        var inner = width - 4;
        var rule = new string('─', width - 2);
        var lines = new List<string> { border("╭" + rule + "╮") };

        foreach (var line in content)
        {
            var fitted = Layout.Fit(line, inner);
            var padding = new string(' ', Math.Max(0, inner - Output.VisibleLength(fitted)));
            lines.Add($"{border("│")} {fitted}{padding} {border("│")}");
        }

        lines.Add(border("╰" + rule + "╯"));
        return lines;
    }
}
