using Microsoft.EntityFrameworkCore;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public partial class OrderService
{
    // US29: same running rule as Home and the cart (PromotionRules); the best promotion wins.
    // Percentage and fixed-price promotions both apply. OriginalPrice and PromoEndsOn are set
    // only while a promotion lowers the price, so the menu can show the normal price struck
    // through and when the offer ends. Admin lists never call this and show the normal price.
    private async Task<IReadOnlyList<MenuItemSummary>> WithCustomerPricesAsync(
        IReadOnlyList<MenuItemSummary> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return items;
        int[] ids = items.Select(item => item.Id).ToArray();
        var rows = await PromotionRules.Running(_context.Promotions.AsNoTracking(), DateTime.Now)
            .Where(p => ids.Contains(p.MenuItemId))
            .Select(p => new PromotionPriceRow(p.MenuItemId, p.MenuItem.Price, p.DiscountPercent, p.PromoPrice, p.EndDate))
            .ToListAsync(cancellationToken);
        var best = PromotionRules.Best(rows);
        return items.Select(item =>
        {
            if (!best.TryGetValue(item.Id, out var promotion)) return item;
            decimal price = OrderPricingRules.DiscountedPrice(item.Price, promotion.Percent);
            return price < item.Price
                ? item with { Price = price, OriginalPrice = item.Price, PromoEndsOn = promotion.EndDate }
                : item;
        }).ToArray();
    }
}
