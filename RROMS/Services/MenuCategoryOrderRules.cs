// Owner: Member 3. Pure ordering rules; no database or presentation dependencies.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RROMS.Models;

namespace RROMS.Services
{
    internal static class MenuCategoryOrderRules
    {
        internal static void Compact(IEnumerable<MenuCategory> rows)
        {
            int position = 0;
            foreach (var row in rows) row.DisplayOrder = checked(++position);
        }

        internal static string Stamp(IEnumerable<(int Id, int Position)> rows)
        {
            string value = string.Join(";", rows.OrderBy(row => row.Id).Select(row =>
                row.Id.ToString(CultureInfo.InvariantCulture) + ":" +
                row.Position.ToString(CultureInfo.InvariantCulture)));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }

        internal static bool IsCompletePermutation(IReadOnlyList<int>? requested,
            IEnumerable<int> current)
        {
            if (requested is null || requested.Any(id => id <= 0)) return false;
            var existing = current.ToHashSet();
            return requested.Count == existing.Count &&
                requested.Distinct().Count() == requested.Count && existing.SetEquals(requested);
        }
    }
}
