namespace Inscribed.Domain.Exceptions;

public sealed class MisconfiguredCollectionException : Exception
{
    public MisconfiguredCollectionException(string key, IReadOnlyList<string> errors)
        : base($"Collection '{key}' is defined but its definition is invalid, so it is not being served. Fix the definition and reload.")
    {
        Key = key;
        Errors = errors;
    }

    public string Key { get; }

    public IReadOnlyList<string> Errors { get; }
}
