namespace Inscribed.Domain.Exceptions;

public sealed record VersionConflict(string Path, int Expected, int Provided);

public sealed class ConcurrencyConflictException : Exception
{
    public IReadOnlyList<VersionConflict> Conflicts { get; }

    public ConcurrencyConflictException(string message) : base(message)
    {
        Conflicts = [];
    }

    public ConcurrencyConflictException(string message, Exception innerException) : base(message, innerException)
    {
        Conflicts = [];
    }

    public ConcurrencyConflictException(string message, IReadOnlyList<VersionConflict> conflicts) : base(message)
    {
        Conflicts = conflicts;
    }
}
