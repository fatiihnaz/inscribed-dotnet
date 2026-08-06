using System.Globalization;
using Inscribed.Domain.Exceptions;

namespace Inscribed.Application.Services.Helpers;

public static class LocaleResolver
{
    public static string? Resolve(IReadOnlyList<string> locales, string? requested, bool forWrite)
    {
        if (locales.Count == 0)
        {
            if (forWrite && !string.IsNullOrWhiteSpace(requested))
                throw new ValidationException([$"Unknown locale '{requested}'; no locales are declared here."]);

            return null;
        }

        if (string.IsNullOrWhiteSpace(requested))
        {
            return locales[0];
        }

        var normalized = requested.Trim().ToLower(CultureInfo.InvariantCulture);

        foreach (var locale in locales)
        {
            if (string.Equals(locale, normalized, StringComparison.Ordinal))
            {
                return locale;
            }
        }

        if (forWrite)
        {
            throw new ValidationException([$"Unknown locale '{requested}'. Available: {string.Join(", ", locales)}."]);
        }

        return locales[0];
    }
}
