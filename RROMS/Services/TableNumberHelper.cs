
using System.Text.RegularExpressions;

namespace RROMS.Services;

public static class TableNumberHelper
{
    // Code letters + digits, e.g. VVIPR001. Older types that were created
    // with a longer name keep working (legacyOk), but new ones must fit.
    public const int MaxLength = 8;

    // Longest code a type may have with this many digits (never more than 6 letters).
    public static int MaxCodeLength(int padding) =>
        Math.Clamp(MaxLength - padding, 1, 6);

    public static string Build(
        string typeCode,
        string number,
        int padding,
        bool legacyOk = false)
    {
        if (padding < 1 || padding > 4)
            throw new ArgumentException(
                "Padding must be between 1 and 4.");

        if (!Regex.IsMatch(
            typeCode, @"^[A-Z]+$"))
            throw new ArgumentException(
                "Invalid table type code.");

        if (!Regex.IsMatch(
            number, @"^[0-9]+$") ||
            number.Length != padding)
            throw new ArgumentException(
                $"Table number must contain exactly " +
                $"{padding} digits.");

        if (!int.TryParse(number, out var value) ||
            value < 1)
            throw new ArgumentException(
                "Table number must be greater than zero.");

        string result = typeCode + number;

        if (!legacyOk && result.Length > MaxLength)
            throw new ArgumentException(
                $"Table name cannot exceed {MaxLength} characters " +
                $"(code {typeCode.Length} + {padding} digits).");

        return result;
    }

    public static string NextNumber(
        string typeCode,
        int padding,
        IEnumerable<string> existingNames,
        bool legacyOk = false)
    {
        int max = (int)Math.Pow(10, padding) - 1;

        var used = existingNames.ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i <= max; i++)
        {
            string number = i.ToString(
                $"D{padding}");

            string fullName =
                Build(typeCode, number, padding, legacyOk);

            if (!used.Contains(fullName))
                return number;
        }

        throw new InvalidOperationException(
            "No available table numbers.");
    }
}
