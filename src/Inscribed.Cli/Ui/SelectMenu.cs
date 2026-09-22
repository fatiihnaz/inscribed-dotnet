namespace Inscribed.Cli.Ui;

internal sealed record MenuItem(string Value, string Label, string Detail = "", string? Group = null, string? Disabled = null, char? Hotkey = null);

internal enum MenuOutcome
{
    Pending,
    Confirmed,
    Cancelled,
}

internal sealed class SelectMenu
{
    private const int ChromeRows = 4;

    private const int MinimumWindow = 2;

    private const int Header = -1;

    private const int Spacer = -2;

    private const string MultipleHint = "↑↓ move · space toggle · enter confirm · esc cancel";

    private const string SingleHint = "↑↓ move · enter select · esc cancel";

    private readonly string _title;
    private readonly IReadOnlyList<MenuItem> _items;
    private readonly bool _multiple;
    private readonly string _lead;
    private readonly string _indent;
    private readonly SortedSet<int> _checked = [];
    private int _cursor;
    private int _offset;

    public SelectMenu(string title, IReadOnlyList<MenuItem> items, bool multiple, string lead = "  ", string indent = "  ", int initial = 0)
    {
        _title = title;
        _items = items;
        _multiple = multiple;
        _lead = lead;
        _indent = indent;
        _cursor = Seek(initial, 1) ?? throw new ArgumentException("A menu needs at least one enabled item.", nameof(items));
    }

    public IReadOnlyList<MenuItem> Selection => [.. _checked.Select(index => _items[index])];

    public IReadOnlyList<MenuItem>? Run(Screen screen)
    {
        try
        {
            while (true)
            {
                screen.Show(Render(screen.Height));

                switch (Handle(screen.ReadKey()))
                {
                    case MenuOutcome.Confirmed:
                        return Selection;
                    case MenuOutcome.Cancelled:
                        return null;
                }
            }
        }
        finally
        {
            screen.Hide();
        }
    }

    public MenuOutcome Handle(ConsoleKeyInfo key)
    {
        if (key.Key is ConsoleKey.Escape || (key.Key is ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            return MenuOutcome.Cancelled;
        }

        if (!_multiple && Hotkey(key.KeyChar) is { } chosen)
        {
            _cursor = chosen;
            _checked.Clear();
            _checked.Add(chosen);
            return MenuOutcome.Confirmed;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _cursor = Seek(_cursor - 1, -1) ?? _cursor;
                break;

            case ConsoleKey.DownArrow:
                _cursor = Seek(_cursor + 1, 1) ?? _cursor;
                break;

            case ConsoleKey.Home:
                _cursor = Seek(0, 1) ?? _cursor;
                break;

            case ConsoleKey.End:
                _cursor = Seek(_items.Count - 1, -1) ?? _cursor;
                break;

            case ConsoleKey.Spacebar when _multiple:
                if (!_checked.Remove(_cursor))
                {
                    _checked.Add(_cursor);
                }

                break;

            case ConsoleKey.Enter:
                if (!_multiple || _checked.Count == 0)
                {
                    _checked.Clear();
                    _checked.Add(_cursor);
                }

                return MenuOutcome.Confirmed;
        }

        return MenuOutcome.Pending;
    }

    public IReadOnlyList<string> Render(int height)
    {
        var rows = Rows();
        var available = Math.Max(2, height - 1);
        var compact = available - ChromeRows < MinimumWindow;
        var window = compact ? available - 1 : available - ChromeRows;

        _offset = rows.Count > window ? Scroll(rows, window) : 0;

        var above = rows.Take(_offset).Count(row => row.Item >= 0);
        var below = rows.Skip(_offset + window).Count(row => row.Item >= 0);
        var lines = new List<string> { _lead + _title };

        if (!compact)
        {
            lines.Add(above > 0 ? _indent + "  " + Output.Dim($"↑ {above} more") : string.Empty);
        }

        lines.AddRange(rows.Skip(_offset).Take(window).Select(row => row.Text));

        if (!compact)
        {
            lines.Add(below > 0 ? _indent + "  " + Output.Dim($"↓ {below} more") : string.Empty);
            lines.Add(_indent + Output.Dim(_multiple ? MultipleHint : SingleHint));
        }

        return lines;
    }

    private int Scroll(List<(string Text, int Item)> rows, int window)
    {
        var cursorRow = rows.FindIndex(row => row.Item == _cursor);
        var top = window > 1 && cursorRow > 0 && rows[cursorRow - 1].Item == Header ? cursorRow - 1 : cursorRow;
        var offset = Math.Clamp(_offset, Math.Max(0, cursorRow - window + 1), top);

        offset = Math.Min(offset, rows.Count - window);

        return rows[offset].Item == Spacer && offset < top ? offset + 1 : offset;
    }

    private List<(string Text, int Item)> Rows()
    {
        var rows = new List<(string Text, int Item)>();
        string? group = null;

        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];

            if (index == 0 || item.Group != group)
            {
                if (index > 0)
                {
                    rows.Add((string.Empty, Spacer));
                }

                if (item.Group is not null)
                {
                    rows.Add((_indent + Output.Dim(item.Group), Header));
                }

                group = item.Group;
            }

            rows.Add((Row(index), index));
        }

        return rows;
    }

    private string Row(int index)
    {
        var item = _items[index];
        var width = _items.Where(other => other.Group == item.Group).Max(other => other.Label.Length);
        var current = index == _cursor;
        var enabled = item.Disabled is null;

        var pointer = current ? Output.Accent("❯") : " ";
        var mark = !_multiple ? string.Empty : _checked.Contains(index) ? Output.Green("●") + " " : Output.Dim("○") + " ";
        var label = item.Detail.Length > 0 || !enabled ? item.Label.PadRight(width) : item.Label;
        var styled = !enabled ? Output.Dim(label) : current ? Output.Accent(label) : label;
        var detail = item.Detail.Length > 0 ? "  " + item.Detail : string.Empty;
        var note = enabled ? string.Empty : "  " + Output.Dim($"({item.Disabled})");

        return $"{_indent}{pointer} {mark}{styled}{detail}{note}".TrimEnd();
    }

    private int? Hotkey(char character)
    {
        for (var index = 0; index < _items.Count; index++)
        {
            if (_items[index] is { Disabled: null, Hotkey: { } hotkey } && char.ToLowerInvariant(hotkey) == char.ToLowerInvariant(character))
            {
                return index;
            }
        }

        return null;
    }

    private int? Seek(int start, int delta)
    {
        for (var step = 0; step < _items.Count; step++)
        {
            var index = ((start + delta * step) % _items.Count + _items.Count) % _items.Count;

            if (_items[index].Disabled is null)
            {
                return index;
            }
        }

        return null;
    }
}
