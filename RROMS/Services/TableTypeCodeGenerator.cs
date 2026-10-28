
using System.Text.RegularExpressions;

namespace RROMS.Services;

public static class TableTypeCodeGenerator
{
    public static string Generate(
    string name,
    IEnumerable<string> existingCodes)
    {
        var words = Regex.Matches(
                name.ToUpperInvariant(),
                @"[A-Z]+")
            .Select(m => m.Value)
            .ToArray();

        if (words.Length == 0)
            throw new ArgumentException(
                "Table type name must contain English letters.");

        var used = existingCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string baseCode = words.Length > 1
            ? string.Concat(words.Select(w => w[0]))
            : words[0][..Math.Min(2, words[0].Length)];

        baseCode = baseCode[..Math.Min(baseCode.Length, 6)];

        if (!used.Contains(baseCode))
            return baseCode;

        string letters = string.Concat(words);

        for (int length = 2;
             length <= Math.Min(letters.Length, 6);
             length++)
        {
            string candidate = letters[..length];

            if (!used.Contains(candidate))
                return candidate;
        }

        // Alphabetic fallback, maximum 6 characters.
        string stem = baseCode[..Math.Min(baseCode.Length, 5)];

        for (char suffix = 'A'; suffix <= 'Z'; suffix++)
        {
            string candidate = stem + suffix;

            if (!used.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            "Unable to generate a unique table type code.");
    }
}
