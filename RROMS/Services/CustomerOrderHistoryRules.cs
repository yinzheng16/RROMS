using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public static class CustomerOrderHistoryRules
{
    private static string Search(string? value)
    { var text = value?.Trim() ?? ""; return text.Length > 80 ? text[..80] : text; }
    public static HistoryFilter Normalize(HistoryFilter input) => new()
    {
        Search = Search(input.Search),
        Status = input.Status is "InProgress" or "Finished" ? input.Status : Enum.GetNames<OrderStatus>().FirstOrDefault(name => string.Equals(name, input.Status, StringComparison.OrdinalIgnoreCase)) ?? "All",
        Dining = input.Dining is "DineIn" or "Takeaway" ? input.Dining : "All",
        From = input.From?.Date, To = input.To?.Date,
        Sort = input.Sort is "oldest" or "total-high" or "total-low" ? input.Sort : "newest",
        PageNumber = input.ShowAll ? 1 : Math.Max(1, input.PageNumber),
        ShowAll = input.ShowAll
    };
    public static string StatusLabel(OrderStatus status) => status == OrderStatus.ReadyToServe ? "Ready to Serve" : status.ToString();
    public static string DiningLabel(OrderType type) => type == OrderType.Takeaway ? "Takeaway" : "Dine-in";
    public static string? SafeImage(string? path) => path is not null && !path.Any(char.IsControl) && !path.Contains('\\') &&
        (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal)) ? path : null;
    public static string? ValidateChoices(IReadOnlyList<HistoryReorderChoice>? choices)
    {
        if (choices is null || choices.Count > 100) return "Choose up to 100 order lines.";
        var selected = choices.Where(line => line.Selected).ToArray();
        if (selected.Length == 0) return "Select at least one dish to reorder.";
        if (selected.Any(line => line.OrderItemId <= 0 || line.Quantity is < 1 or > 100))
            return "Use a whole quantity from 1 to 100 for each selected dish.";
        if (selected.Select(line => line.OrderItemId).Distinct().Count() != selected.Length)
            return "Choose each order line only once.";
        return null;
    }

    // Validate the entire batch before changing tracked cart entities.
    public static string? CapacityError(IEnumerable<(int MenuItemId, string? Notes, int Quantity)> existing,
        IReadOnlyList<HistoryReorderLine> additions)
    {
        var combined = existing.Concat(additions.Where(line => line.Available)
            .Select(line => (line.MenuItemId, line.Notes, line.Quantity)))
            .GroupBy(line => (line.MenuItemId, Notes: OrderPricingRules.NormalizeNote(line.Notes))).ToArray();
        if (combined.Length > 100) return "This would exceed 100 different dish and note combinations. Edit your cart first.";
        if (combined.Any(group => group.Sum(line => (long)line.Quantity) > 100))
            return "This would exceed 100 portions for a dish and note combination. Reduce your selection or edit your cart.";
        return null;
    }
}
