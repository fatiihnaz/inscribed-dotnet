namespace Inscribed.Application.Contracts.Repositories;

public sealed record CollectionSearch(IReadOnlyList<string> Terms)
{
    public string Phrase => string.Join(' ', Terms);
}
