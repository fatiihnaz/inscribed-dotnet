using Inscribed.Application.Contracts.Repositories;

namespace Inscribed.Application.Services.Helpers;

public static class CollectionSearchParser
{
    private const int MaxTerms = 8;

    public static CollectionSearch? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var terms = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return new CollectionSearch([.. terms.Take(MaxTerms)]);
    }
}
