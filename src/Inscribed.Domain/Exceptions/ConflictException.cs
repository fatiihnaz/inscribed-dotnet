namespace Inscribed.Domain.Exceptions;

public sealed class ConflictException : Exception
{
    public string? Reason { get; }

    public string? ConflictingSlug { get; }

    public ConflictException(string message) : base(message) { }

    public ConflictException(string message, string reason, string? conflictingSlug = null) : base(message)
    {
        Reason = reason;
        ConflictingSlug = conflictingSlug;
    }
}
