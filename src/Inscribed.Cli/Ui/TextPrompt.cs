namespace Inscribed.Cli.Ui;

internal static class TextPrompt
{
    public static string? Read(Screen screen, string prefix, string indent, string label, string? suggestion, bool optional)
    {
        var buffer = new LineBuffer();
        var insisted = false;

        while (true)
        {
            var head = $"{prefix}{label}: ";
            var available = Math.Max(1, screen.Width - 2 - Output.VisibleLength(head));
            var start = Math.Max(0, buffer.Cursor - available + 1);
            var text = buffer.Text;
            var shown = buffer.Length == 0 && suggestion is not null
                ? Output.Dim(suggestion)
                : text.Substring(start, Math.Min(available, text.Length - start));

            screen.Show(
                [head + shown, indent + Hint(buffer, suggestion, optional, insisted)],
                (0, Output.VisibleLength(head) + buffer.Cursor - start));

            var key = screen.ReadKey();

            if (key.Key is ConsoleKey.Escape || (key.Key is ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                screen.Hide();
                throw new PromptCancelledException();
            }

            if (key.Key is ConsoleKey.Enter)
            {
                var answer = buffer.Length > 0 ? buffer.Text.Trim() : suggestion;

                if (!string.IsNullOrWhiteSpace(answer) || optional)
                {
                    screen.Hide();
                    return string.IsNullOrWhiteSpace(answer) ? null : answer;
                }

                insisted = true;
                continue;
            }

            if (key.Key is ConsoleKey.Tab && buffer.Length == 0 && suggestion is not null)
            {
                buffer.Set(suggestion);
                continue;
            }

            if (buffer.Edit(key))
            {
                insisted = false;
            }
        }
    }

    private static string Hint(LineBuffer buffer, string? suggestion, bool optional, bool insisted) =>
        insisted ? Output.Yellow("a value is required · esc to cancel")
        : buffer.Length == 0 && suggestion is not null ? Output.Dim("enter to accept · tab to edit · esc to cancel")
        : buffer.Length == 0 && optional ? Output.Dim("optional · enter to skip · esc to cancel")
        : Output.Dim("enter to confirm · esc to cancel");
}
