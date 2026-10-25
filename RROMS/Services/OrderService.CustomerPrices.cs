using Microsoft.EntityFrameworkCore;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public partial class OrderService
{
    // Same activity rule as Home: active, started, end DATE not passed;
    // use the strongest active discount. Admin always retains the base price.
    private async Task<IReadOnlyList<MenuItemSummary>> WithCustomerPricesAsync(
        IReadOnlyList<MenuItemSummary> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return items;
        var now = DateTime.Now;
        int[] ids = items.Select(item => item.Id).ToArray();
        var discounts = await _context.Promotions.AsNoTracking()
            .Where(p => ids.Contains(p.MenuItemId) && p.IsActive && p.StartDate <= now && p.EndDate >= now.Date)
            .GroupBy(p => p.MenuItemId)
            .Select(group => new { Id = group.Key, Percent = group.Max(p => p.DiscountPercent) })
            .ToDictionaryAsync(row => row.Id, row => row.Percent, cancellationToken);
        return items.Select(item => item with { Price = OrderPricingRules.DiscountedPrice(item.Price,
            discounts.GetValueOrDefault(item.Id)) }).ToArray();
    }
}
