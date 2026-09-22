using System.Text;

namespace Inscribed.Cli.Ui;

internal sealed class BlockWriter : TextWriter
{
    private readonly Block _block;
    private readonly StringBuilder _pending = new();
    private readonly Lock _gate = new();

    public BlockWriter(Block block) => _block = block;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_gate)
        {
            Append(value);
        }
    }

    public override void Write(string? value)
    {
        lock (_gate)
        {
            foreach (var character in value ?? string.Empty)
            {
                Append(character);
            }
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (_pending.Length > 0)
            {
                _block.Line(_pending.ToString());
                _pending.Clear();
            }
        }
    }

    private void Append(char value)
    {
        if (value != '\n')
        {
            _pending.Append(value);
            return;
        }

        _block.Line(_pending.ToString().TrimEnd('\r'));
        _pending.Clear();
    }
}
