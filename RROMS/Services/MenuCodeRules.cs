using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RROMS.Services
{
    // Owner: Member 3. Pure rules shared by allocation and verification.
    public static class MenuCodeRules
    {
        // Reserve these even before those categories exist: a new "Sakana"
        // category must not take SA away from Sarada or SS from Sashimi.
        public static IReadOnlyDictionary<string, string> KnownPrefixes { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OTSUMAMI"] = "OT", ["SMALLBITES"] = "OT",
                ["SARADA"] = "SA", ["SALADS"] = "SA",
                ["SASHIMI"] = "SS", ["RAWSEAFOOD"] = "SS",
                ["SUSHI"] = "SU",
                ["MAKIMONO"] = "MA", ["SUSHIROLLS"] = "MA",
                ["YAKITORI"] = "YT", ["CHICKENSKEWERS"] = "YT",
                ["YAKIMONO"] = "YM", ["GRILLEDDISHES"] = "YM",
                ["AGEMONO"] = "AG", ["FRIEDDISHES"] = "AG",
                ["TEMPURA"] = "TE",
                ["DONMONO"] = "DO", ["DONBURI"] = "DO", ["RICEBOWLS"] = "DO",
                ["GOHAN"] = "GO", ["RICEDISHES"] = "GO",
                ["MENMONO"] = "ME", ["NOODLES"] = "ME",
                ["WANMONO"] = "WA", ["SOUPSSTEAMEDDISHES"] = "WA",
                ["KANMI"] = "KA", ["DESSERTS"] = "KA",
                ["ARUKORU"] = "AL", ["ALCOHOLICDRINKS"] = "AL",
                ["SOFUTODORINKU"] = "SD", ["NONALCOHOLICDRINKS"] = "SD"
            };

        public static string CategoryKey(string name)
        {
            string primary = name.Split('·', 2)[0].Normalize(NormalizationForm.FormD);
            return new string(primary.Where(ch => ch is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z' or >= '0' and <= '9').ToArray()).ToUpperInvariant();
        }

        public static string CategoryCode(string name, IEnumerable<string> issued)
        {
            var used = new HashSet<string>(issued, StringComparer.OrdinalIgnoreCase);
            var reserved = new HashSet<string>(KnownPrefixes.Values, StringComparer.Ordinal);
            string key = CategoryKey(name);
            if (KnownPrefixes.TryGetValue(key, out string? known) && !used.Contains(known))
                return known;

            string letters = new string(key.Where(ch => ch is >= 'A' and <= 'Z').ToArray());
            char first = letters.Length == 0 ? 'C' : letters[0];
            foreach (char second in letters.Skip(1).Append('X'))
            {
                string candidate = string.Concat(first, second);
                if (!used.Contains(candidate) && !reserved.Contains(candidate)) return candidate;
            }
            for (char left = 'A'; left <= 'Z'; left++)
                for (char right = 'A'; right <= 'Z'; right++)
                {
                    string candidate = string.Concat(left, right);
                    if (!used.Contains(candidate) && !reserved.Contains(candidate)) return candidate;
                }
            // Extremely large catalogues can outgrow the two-letter namespace.
            for (int number = 1; number < int.MaxValue; number++)
            {
                // Prefixes remain letters only: the letter/number boundary in
                // an item code stays unambiguous even after two-letter codes run out.
                string suffix = "";
                for (int value = number; value > 0; value = (value - 1) / 26)
                    suffix = (char)('A' + (value - 1) % 26) + suffix;
                string candidate = "MC" + suffix;
                if (!used.Contains(candidate)) return candidate;
            }
            throw new OverflowException("No category code is available.");
        }

        public static (string Code, int Number) NextItemCode(string prefix, int lastNumber)
        {
            if (lastNumber < 0) throw new ArgumentOutOfRangeException(nameof(lastNumber));
            int next = checked(lastNumber + 1);
            return (prefix + next.ToString("D2", CultureInfo.InvariantCulture), next);
        }
    }
}
