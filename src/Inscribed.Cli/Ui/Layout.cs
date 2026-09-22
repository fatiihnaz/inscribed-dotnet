using System.Text;

namespace Inscribed.Cli.Ui;

internal static class Layout
{
    private const char Ellipsis = '…';

    public static string Fit(string line, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (Output.VisibleLength(line) <= width)
        {
            return line;
        }

        var builder = new StringBuilder(line.Length);
        var visible = 0;
        var escaped = false;

        foreach (var character in line)
        {
            if (escaped || character == '\u001b')
            {
                builder.Append(character);
                escaped = character == '\u001b' || character is not 'm';
                continue;
            }

            if (visible == width - 1)
            {
                break;
            }

            builder.Append(character);
            visible++;
        }

        return builder.Append(Ellipsis).Append(Ansi.Reset).ToString();
    }

    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();

        if (width <= 0 || Output.VisibleLength(text) <= width)
        {
            lines.Add(text);
            return lines;
        }

        var line = new StringBuilder();
        var sequence = new StringBuilder();
        var style = string.Empty;
        var visible = 0;
        var breakAt = -1;
        var breakVisible = 0;
        var breakStyle = string.Empty;

        foreach (var character in text)
        {
            if (sequence.Length > 0 || character == '\u001b')
            {
                sequence.Append(character);

                if (character is 'm')
                {
                    var code = sequence.ToString();
                    style = code == Ansi.Reset ? string.Empty : style + code;
                    line.Append(code);
                    sequence.Clear();
                }

                continue;
            }

            if (visible == width)
            {
                if (character == ' ' || breakAt <= 0 || breakVisible < width / 2)
                {
                    lines.Add(Close(line.ToString(), style));
                    line.Clear().Append(style);
                    visible = 0;
                    breakAt = -1;

                    if (character == ' ')
                    {
                        continue;
                    }
                }
                else
                {
                    var rest = line.ToString(breakAt + 1, line.Length - breakAt - 1);
                    lines.Add(Close(line.ToString(0, breakAt), breakStyle));
                    line.Clear().Append(breakStyle).Append(rest);
                    visible -= breakVisible + 1;
                    breakAt = -1;
                }
            }

            if (character == ' ')
            {
                breakAt = line.Length;
                breakVisible = visible;
                breakStyle = style;
            }

            line.Append(character);
            visible++;
        }

        lines.Add(line.ToString());
        return lines;
    }

    private static string Close(string chunk, string style) => style.Length > 0 ? chunk + Ansi.Reset : chunk;
}
