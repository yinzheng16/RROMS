using System;
using System.Linq;

namespace RROMS.Services
{
    // Owner: Member 3. Comparison only; the saved display name keeps its spaces.
    internal static class MenuItemNameRules
    {
        internal static string DuplicateKey(string name)
            => new string(name.Where(character => !char.IsWhiteSpace(character)).ToArray())
                .ToUpperInvariant();
    }
}
