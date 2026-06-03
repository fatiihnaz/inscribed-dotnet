using System.Globalization;

namespace Inscribed.Application.Services.Helpers;

public static class SlugNormalizer
{
    public static string NormalizeSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var trimmed = slug.Trim();
        var lowered = trimmed.ToLower(CultureInfo.InvariantCulture);

        if (!lowered.StartsWith('/'))
        {
            lowered = "/" + lowered;
        }

        if (lowered.Length > 1 && lowered.EndsWith('/'))
        {
            lowered = lowered.TrimEnd('/');
        }

        return lowered;
    }

    public static string NormalizeBlockPath(string blockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockPath);
        return blockPath.Trim().ToLower(CultureInfo.InvariantCulture);
    }
}