namespace RROMS.Services;

// Owner: Member 2 (Tables & Reservations)
// Picks the dining area icon the customer pages show. The names are the Lucide icons an admin can
// choose for a dining area (Admin > Tables); the same names are drawn in Pages/Customer/Reservations/_AreaIcons.cshtml.
public static class AreaIconRules
{
    public const string Fallback = "map-pin";

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "wine", "utensils-crossed", "door-closed", "trees", "sunset", "coffee", "sofa", "layout-grid", "map-pin"
    };

    // The icon the admin saved for the area. An area without one gets the same guess as the admin page (from its name).
    public static string Resolve(string? iconName, string? areaName)
    {
        var icon = iconName?.Trim();
        if (!string.IsNullOrEmpty(icon))
            return Known.Contains(icon) ? icon.ToLowerInvariant() : Fallback;

        var name = (areaName ?? "").ToLowerInvariant();
        return name.Contains("bar") || name.Contains("counter") ? "wine" :
               name.Contains("private") || name.Contains("vip") || name.Contains("room") ? "door-closed" :
               name.Contains("outdoor") || name.Contains("garden") || name.Contains("terrace") ? "trees" :
               name.Contains("roof") ? "sunset" :
               name.Contains("cafe") || name.Contains("coffee") ? "coffee" :
               name.Contains("lounge") ? "sofa" :
               name.Contains("hall") || name.Contains("dining") ? "utensils-crossed" :
               "layout-grid";
    }
}
