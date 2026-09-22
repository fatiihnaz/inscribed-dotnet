using System.Text;

namespace Inscribed.Cli.Ui;

internal sealed class LineBuffer
{
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public int Cursor { get; private set; }

    public int Length => _text.Length;

    public void Set(string text)
    {
        _text.Clear().Append(text);
        Cursor = _text.Length;
    }

    public void Replace(int start, string value)
    {
        _text.Remove(start, Cursor - start).Insert(start, value);
        Cursor = start + value.Length;
    }

    public bool Edit(ConsoleKeyInfo key)
    {
        if (key.KeyChar is not '\0' && !char.IsControl(key.KeyChar))
        {
            _text.Insert(Cursor++, key.KeyChar);
            return true;
        }

        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);

        switch (key.Key)
        {
            case ConsoleKey.Backspace or ConsoleKey.W when control:
                Delete(WordStart(), Cursor);
                return true;

            case ConsoleKey.Backspace when Cursor > 0:
                Delete(Cursor - 1, Cursor);
                return true;

            case ConsoleKey.Delete when Cursor < _text.Length:
                _text.Remove(Cursor, 1);
                return true;

            case ConsoleKey.LeftArrow when control:
                Cursor = WordStart();
                return true;

            case ConsoleKey.RightArrow when control:
                Cursor = WordEnd();
                return true;

            case ConsoleKey.LeftArrow when Cursor > 0:
                Cursor--;
                return true;

            case ConsoleKey.RightArrow when Cursor < _text.Length:
                Cursor++;
                return true;

            case ConsoleKey.Home:
            case ConsoleKey.A when control:
                Cursor = 0;
                return true;

            case ConsoleKey.End:
            case ConsoleKey.E when control:
                Cursor = _text.Length;
                return true;

            case ConsoleKey.U when control:
                Delete(0, Cursor);
                return true;

            case ConsoleKey.K when control:
                _text.Length = Cursor;
                return true;

            default:
                return false;
        }
    }

    private void Delete(int start, int end)
    {
        _text.Remove(start, end - start);
        Cursor = start;
    }

    private int WordStart()
    {
        var index = Cursor;

        while (index > 0 && _text[index - 1] == ' ')
        {
            index--;
        }

        while (index > 0 && _text[index - 1] != ' ')
        {
            index--;
        }

        return index;
    }

    private int WordEnd()
    {
        var index = Cursor;

        while (index < _text.Length && _text[index] == ' ')
        {
            index++;
        }

        while (index < _text.Length && _text[index] != ' ')
        {
            index++;
        }

        return index;
    }
}
