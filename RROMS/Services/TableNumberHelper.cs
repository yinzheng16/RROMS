
using System.Text.RegularExpressions;

namespace RROMS.Services;

public static class TableNumberHelper
{
    public static string Build(
        string typeCode,
        string number,
        int padding)
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

        if (result.Length > 10)
            throw new ArgumentException(
                "Table name cannot exceed 10 characters.");

        return result;
    }

    public static string NextNumber(
        string typeCode,
        int padding,
        IEnumerable<string> existingNames)
    {
        int max = (int)Math.Pow(10, padding) - 1;

        var used = existingNames.ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i <= max; i++)
        {
            string number = i.ToString(
                $"D{padding}");

            string fullName =
                Build(typeCode, number, padding);

            if (!used.Contains(fullName))
                return number;
        }

        throw new InvalidOperationException(
            "No available table numbers.");
    }
}
