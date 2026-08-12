namespace Inscribed.Domain.Exceptions;

public sealed class ArchivedException : Exception
{
    public string Path { get; }

    public int Version { get; }

    public ArchivedException(string path, int version)
        : base($"Item '{path}' is archived; restore it before writing to it.")
    {
        Path = path;
        Version = version;
    }
}
