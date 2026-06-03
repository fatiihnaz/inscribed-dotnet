using System.Text;

namespace Inscribed.Application.Services.Helpers;

public static class SlugGenerator
{
    private const int MaxLength = 80;

    private static readonly Dictionary<char, char> TurkishMap = new()
    {
        ['ç'] = 'c', ['Ç'] = 'c',
        ['ğ'] = 'g', ['Ğ'] = 'g',
        ['ı'] = 'i', ['I'] = 'i',
        ['İ'] = 'i',
        ['ö'] = 'o', ['Ö'] = 'o',
        ['ş'] = 's', ['Ş'] = 's',
        ['ü'] = 'u', ['Ü'] = 'u',
    };

    public static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var c = TurkishMap.TryGetValue(ch, out var mapped) ? mapped : char.ToLowerInvariant(ch);

            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        var result = sb.ToString().Trim('-');
        if (result.Length > MaxLength)
            result = result[..MaxLength].TrimEnd('-');

        return result;
    }
}