
using System.Text.RegularExpressions;

namespace RROMS.Services;

public static class TableTypeCodeGenerator
{
    public static string Generate(
    string name,
    IEnumerable<string> existingCodes,
    int maxLength = 6)
    {
        maxLength = Math.Clamp(maxLength, 1, 6);

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

        baseCode = baseCode[..Math.Min(baseCode.Length, maxLength)];

        if (!used.Contains(baseCode))
            return baseCode;

        string letters = string.Concat(words);

        for (int length = 2;
             length <= Math.Min(letters.Length, maxLength);
             length++)
        {
            string candidate = letters[..length];

            if (!used.Contains(candidate))
                return candidate;
        }

        // Alphabetic fallback, never longer than maxLength.
        string stem = baseCode[..Math.Min(baseCode.Length, Math.Max(1, maxLength - 1))];

        for (char suffix = 'A'; suffix <= 'Z'; suffix++)
        {
            string candidate = (maxLength == 1 ? "" : stem) + suffix;

            if (!used.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            "Unable to generate a unique table type code.");
    }
}
