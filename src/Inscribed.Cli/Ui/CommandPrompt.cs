namespace Inscribed.Cli.Ui;

internal sealed record Suggestion(string Value, string Detail = "", bool Final = true);

internal sealed class CommandPrompt
{
    public const int MinimumHeight = 4;

    private const int MaxSuggestions = 8;

    private const string Marker = "> ";

    private readonly Screen _screen;
    private readonly History _history;
    private readonly Func<string, int, (int Start, IReadOnlyList<Suggestion> Items)> _suggest;

    public CommandPrompt(Screen screen, History history, Func<string, int, (int Start, IReadOnlyList<Suggestion> Items)> suggest)
    {
        _screen = screen;
        _history = history;
        _suggest = suggest;
    }

    public string? Read(string? context)
    {
        var buffer = new LineBuffer();
        var browsing = _history.Entries.Count;
        var draft = string.Empty;
        var selected = 0;
        var navigated = false;
        var dismissed = false;
        var exitArmed = false;

        while (true)
        {
            var (start, items) = Suggestions(buffer, dismissed);
            selected = items.Count == 0 ? 0 : Math.Clamp(selected, 0, items.Count - 1);

            Render(buffer, items, selected, context, exitArmed);

            var key = _screen.ReadKey();
            var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);

            if (key.Key is ConsoleKey.C && control)
            {
                if (buffer.Length > 0)
                {
                    buffer.Set(string.Empty);
                    exitArmed = false;
                    continue;
                }

                if (exitArmed)
                {
                    _screen.Hide();
                    return null;
                }

                exitArmed = true;
                continue;
            }

            if (key.Key is ConsoleKey.D && control && buffer.Length == 0)
            {
                _screen.Hide();
                return null;
            }

            exitArmed = false;

            switch (key.Key)
            {
                case ConsoleKey.Enter when items.Count > 0 && navigated:
                case ConsoleKey.Tab when items.Count > 0:
                    buffer.Replace(start, items[selected].Value + (items[selected].Final ? " " : string.Empty));
                    selected = 0;
                    navigated = false;
                    continue;

                case ConsoleKey.Enter:
                    _screen.Hide();
                    _history.Add(buffer.Text);
                    return buffer.Text;

                case ConsoleKey.UpArrow when items.Count > 0:
                    selected = (selected + items.Count - 1) % items.Count;
                    navigated = true;
                    continue;

                case ConsoleKey.DownArrow when items.Count > 0:
                    selected = (selected + 1) % items.Count;
                    navigated = true;
                    continue;

                case ConsoleKey.UpArrow when browsing > 0:
                    if (browsing == _history.Entries.Count)
                    {
                        draft = buffer.Text;
                    }

                    buffer.Set(_history.Entries[--browsing]);
                    dismissed = true;
                    continue;

                case ConsoleKey.DownArrow when browsing < _history.Entries.Count:
                    browsing++;
                    buffer.Set(browsing == _history.Entries.Count ? draft : _history.Entries[browsing]);
                    dismissed = true;
                    continue;

                case ConsoleKey.Escape when items.Count > 0:
                    dismissed = true;
                    continue;

                case ConsoleKey.Escape:
                    buffer.Set(string.Empty);
                    continue;
            }

            if (buffer.Edit(key))
            {
                dismissed = false;
                selected = 0;
                navigated = false;
            }
        }
    }

    private (int Start, IReadOnlyList<Suggestion> Items) Suggestions(LineBuffer buffer, bool dismissed)
    {
        if (dismissed || buffer.Length == 0)
        {
            return (buffer.Cursor, Array.Empty<Suggestion>());
        }

        var (start, items) = _suggest(buffer.Text, buffer.Cursor);
        var token = buffer.Text[start..buffer.Cursor];

        return items is [var only] && string.Equals(only.Value, token, StringComparison.OrdinalIgnoreCase)
            ? (start, Array.Empty<Suggestion>())
            : (start, items);
    }

    private void Render(LineBuffer buffer, IReadOnlyList<Suggestion> items, int selected, string? context, bool exitArmed)
    {
        var width = _screen.Width - 1;
        var inner = Math.Max(1, width - 4 - Marker.Length);
        var offset = Math.Max(0, buffer.Cursor - inner + 1);
        var text = buffer.Text;
        var shown = buffer.Length == 0
            ? Output.Dim("type a command, or help")
            : text.Substring(offset, Math.Min(inner, text.Length - offset));

        var rows = Math.Min(Math.Min(items.Count, MaxSuggestions), Math.Max(0, _screen.Height - MinimumHeight - 1));
        var first = Math.Clamp(selected - rows + 1, 0, Math.Max(0, items.Count - rows));
        var visible = items.Skip(first).Take(rows).ToList();
        var labelWidth = visible.Select(item => item.Value.Length).DefaultIfEmpty(0).Max();
        var lines = new List<string>();

        for (var index = 0; index < visible.Count; index++)
        {
            var label = visible[index].Value.PadRight(labelWidth);
            var styled = first + index == selected ? Output.Accent(label) : label;
            lines.Add($"  {styled}  {Output.Dim(visible[index].Detail)}".TrimEnd());
        }

        lines.AddRange(Box.Draw([Output.Accent(">") + " " + shown], width, Output.Dim));
        lines.Add(Footer(context, items.Count > 0, exitArmed, width));
        _screen.Show(lines, (visible.Count + 1, 2 + Marker.Length + buffer.Cursor - offset), anchored: true);
    }

    private static string Footer(string? context, bool suggesting, bool exitArmed, int width)
    {
        var left = context is null ? Output.Dim("no tenant selected") : $"{Output.Accent("◆")} {context}";
        var right = exitArmed
            ? Output.Yellow("press ctrl+c again to exit")
            : Output.Dim(suggesting ? "↑↓ select · tab accept · esc dismiss" : "tab complete · ↑↓ history · ctrl+d exit");
        var gap = width - 4 - Output.VisibleLength(left) - Output.VisibleLength(right);

        if (gap < 2)
        {
            return "  " + (context is null ? right : left);
        }

        return $"  {left}{new string(' ', gap)}{right}";
    }
}
