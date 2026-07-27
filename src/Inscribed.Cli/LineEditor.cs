using System.Text;

namespace Inscribed.Cli;

internal sealed class LineEditor
{
    private readonly List<string> _history = [];
    private readonly Func<string, int, IReadOnlyList<string>> _complete;

    public LineEditor(Func<string, int, IReadOnlyList<string>> complete) => _complete = complete;

    public string? Read(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.Write(prompt);
            return Console.ReadLine();
        }

        var buffer = new StringBuilder();
        var cursor = 0;
        var browsing = _history.Count;
        var pending = string.Empty;

        Render(prompt, buffer.ToString(), cursor);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key is ConsoleKey.C)
            {
                Console.Error.WriteLine();
                return string.Empty;
            }

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key is ConsoleKey.D && buffer.Length == 0)
            {
                Console.Error.WriteLine();
                return null;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.Error.WriteLine();
                    var line = buffer.ToString();
                    if (line.Trim().Length > 0 && (_history.Count == 0 || _history[^1] != line))
                    {
                        _history.Add(line);
                    }

                    return line;

                case ConsoleKey.Backspace when cursor > 0:
                    buffer.Remove(--cursor, 1);
                    break;

                case ConsoleKey.Delete when cursor < buffer.Length:
                    buffer.Remove(cursor, 1);
                    break;

                case ConsoleKey.LeftArrow when cursor > 0:
                    cursor--;
                    break;

                case ConsoleKey.RightArrow when cursor < buffer.Length:
                    cursor++;
                    break;

                case ConsoleKey.Home:
                    cursor = 0;
                    break;

                case ConsoleKey.End:
                    cursor = buffer.Length;
                    break;

                case ConsoleKey.UpArrow when browsing > 0:
                    if (browsing == _history.Count)
                    {
                        pending = buffer.ToString();
                    }

                    browsing--;
                    buffer.Clear().Append(_history[browsing]);
                    cursor = buffer.Length;
                    break;

                case ConsoleKey.DownArrow when browsing < _history.Count:
                    browsing++;
                    buffer.Clear().Append(browsing == _history.Count ? pending : _history[browsing]);
                    cursor = buffer.Length;
                    break;

                case ConsoleKey.Tab:
                    cursor = Complete(prompt, buffer, cursor);
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(cursor++, key.KeyChar);
                    }

                    break;
            }

            Render(prompt, buffer.ToString(), cursor);
        }
    }

    private int Complete(string prompt, StringBuilder buffer, int cursor)
    {
        var line = buffer.ToString();
        var start = line.LastIndexOf(' ', Math.Max(0, cursor - 1)) + 1;
        if (cursor == 0 || line.Length == 0)
        {
            start = cursor;
        }

        var token = line[start..cursor];
        var candidates = _complete(line, cursor);
        if (candidates.Count == 0)
        {
            return cursor;
        }

        var shared = CommonPrefix(candidates);
        if (candidates.Count == 1)
        {
            shared += " ";
        }

        if (shared.Length > token.Length)
        {
            buffer.Remove(start, cursor - start).Insert(start, shared);
            return start + shared.Length;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(Output.Indent + string.Join("  ", candidates.Select(Output.Dim)));
        Render(prompt, buffer.ToString(), cursor);
        return cursor;
    }

    private static string CommonPrefix(IReadOnlyList<string> values)
    {
        var prefix = values[0];

        foreach (var value in values.Skip(1))
        {
            while (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                prefix = prefix[..^1];
            }
        }

        return prefix;
    }

    private static void Render(string prompt, string text, int cursor)
    {
        Console.Error.Write($"\r\u001b[K{prompt}{text}");

        var back = text.Length - cursor;
        if (back > 0)
        {
            Console.Error.Write($"\u001b[{back}D");
        }
    }
}
