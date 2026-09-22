namespace Inscribed.Cli.Ui;

internal sealed class History
{
    private const int Capacity = 500;

    private readonly List<string> _entries = [];
    private string? _path;

    public History(string? path)
    {
        _path = path;

        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(path).Where(line => line.Trim().Length > 0).ToList();
            _entries.AddRange(lines.Skip(Math.Max(0, lines.Count - Capacity)));

            if (lines.Count > Capacity * 2)
            {
                File.WriteAllLines(path, _entries);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _path = null;
        }
    }

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string line)
    {
        if (line.Trim().Length == 0 || (_entries.Count > 0 && _entries[^1] == line))
        {
            return;
        }

        _entries.Add(line);

        if (_entries.Count > Capacity)
        {
            _entries.RemoveAt(0);
        }

        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _path = null;
        }
    }
}
