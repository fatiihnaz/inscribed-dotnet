namespace Inscribed.Cli.Ui;

internal sealed class Block
{
    private readonly Screen _screen;
    private readonly Lock _gate = new();
    private bool _blankPending;

    public Block(Screen screen) => _screen = screen;

    public static string Indent => "     ";

    public bool Started { get; private set; }

    public string Prefix => Started ? Indent : $"  {Output.Dim("⎿")}  ";

    public void Line(string text)
    {
        lock (_gate)
        {
            if (Output.VisibleLength(text.Trim()) == 0)
            {
                _blankPending = Started;
                return;
            }

            var chunks = Layout.Wrap(text, Math.Max(10, _screen.Width - 1 - Indent.Length));
            var lines = string.Join('\n', chunks.Select((chunk, index) => (index == 0 ? Prefix : Indent) + chunk));

            _screen.Print(_blankPending ? "\n" + lines : lines);
            _blankPending = false;
            Started = true;
        }
    }
}
