using System.Text;

namespace Inscribed.Cli.Ui;

internal sealed class Screen
{
    private readonly ITerminal _terminal;
    private readonly Lock _gate = new();
    private IReadOnlyList<string> _live = [];
    private (int Row, int Column)? _cursor;
    private bool _anchored;
    private int _row;
    private int _log;
    private int _top;

    public Screen(ITerminal terminal) => _terminal = terminal;

    public int Width => _terminal.Width;

    public int Height => _terminal.Height;

    public ConsoleKeyInfo ReadKey() => _terminal.ReadKey();

    public void Print(string text)
    {
        lock (_gate)
        {
            Clamp();

            var width = Math.Max(1, Width - 1);
            var lines = text.Split('\n').Select(line => Layout.Fit(line.TrimEnd('\r'), width)).ToList();
            var detached = _live.Count > 0 && _anchored && _log + lines.Count <= _top;
            var frame = new StringBuilder(detached ? string.Empty : EraseRegion());

            frame.Append(MoveTo(_log));

            foreach (var line in lines)
            {
                frame.Append(line).Append("\r\n");
                Advance();
            }

            _log = _row;
            frame.Append(detached ? PlaceCursor() : DrawRegion());
            Emit(frame.ToString());
        }
    }

    public void Show(IReadOnlyList<string> lines, (int Row, int Column)? cursor = null, bool anchored = false)
    {
        lock (_gate)
        {
            Clamp();

            var frame = new StringBuilder(EraseRegion());

            _live = lines;
            _cursor = cursor;
            _anchored = anchored;
            frame.Append(DrawRegion());
            Emit(frame.ToString());
        }
    }

    public void Hide() => Show([]);

    public void Clear()
    {
        lock (_gate)
        {
            _live = [];
            _cursor = null;
            _row = 0;
            _log = 0;
            _top = 0;
            _terminal.Write(Ansi.ClearScreen);
        }
    }

    private string EraseRegion() => MoveTo(_top) + Ansi.ClearBelow;

    private string DrawRegion()
    {
        var count = _live.Count;

        if (count == 0)
        {
            _top = _log;
            return MoveTo(_log);
        }

        var height = Height;
        var top = _anchored ? Math.Max(_log, height - count) : _log;
        var frame = new StringBuilder(MoveTo(top));
        var width = Math.Max(1, Width - 1);

        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                frame.Append("\r\n");
                Advance();
            }

            frame.Append(Layout.Fit(_live[index], width));
        }

        _log = Math.Max(0, _log - Math.Max(0, top + count - height));
        _top = Math.Max(0, _row - (count - 1));
        frame.Append(PlaceCursor());
        return frame.ToString();
    }

    private string PlaceCursor()
    {
        if (_cursor is not { } position || _live.Count == 0)
        {
            return string.Empty;
        }

        var row = _top + Math.Clamp(position.Row, 0, _live.Count - 1);
        return MoveTo(row) + Ansi.Right(Math.Min(position.Column, Math.Max(1, Width - 1)));
    }

    private string MoveTo(int row)
    {
        _row = row;
        return Ansi.To(row);
    }

    private void Advance() => _row = Math.Min(_row + 1, Math.Max(0, Height - 1));

    private void Clamp()
    {
        var bottom = Math.Max(0, Height - 1);

        _row = Math.Min(_row, bottom);
        _log = Math.Min(_log, bottom);
        _top = Math.Min(_top, bottom);
    }

    private void Emit(string frame) =>
        _terminal.Write(Ansi.HideCursor + frame + (_cursor is null ? string.Empty : Ansi.ShowCursor));
}
