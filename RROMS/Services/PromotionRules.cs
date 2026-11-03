using RROMS.Models;

namespace RROMS.Services;

// Owner: Member 3 (US29). The single rule for which promotions count and what they cost.
// Used by the customer menu, the cart/checkout and the home page so all show the same price.
public static class PromotionRules
{
    // Running = switched on, already started, and the end DATE is today or later
    // (a promotion ending 25 Oct still runs for the whole of 25 Oct, then stops by itself).
    public static IQueryable<Promotion> Running(IQueryable<Promotion> promotions, DateTime now)
    {
        var today = now.Date;
        return promotions.Where(p => p.IsActive && p.StartDate <= now && p.EndDate >= today);
    }

    // Price of the dish under one promotion. A fixed price never goes above the normal price.
    public static decimal PriceUnder(decimal basePrice, decimal discountPercent, decimal? promoPrice)
        => promoPrice is decimal fixedPrice
            ? OrderPricingRules.Money(Math.Clamp(fixedPrice, 0m, basePrice))
            : OrderPricingRules.DiscountedPrice(basePrice, discountPercent);

    // The same promotion as a percentage of today's normal price, for "x% off" labels and for
    // code that works in percentages. DiscountedPrice(basePrice, this) gives PriceUnder(...).
    public static decimal EffectivePercent(decimal basePrice, decimal discountPercent, decimal? promoPrice)
    {
        if (promoPrice is not decimal fixedPrice) return Math.Clamp(discountPercent, 0m, 100m);
        if (basePrice <= 0) return 0m;
        return Math.Clamp((basePrice - Math.Min(fixedPrice, basePrice)) / basePrice * 100m, 0m, 100m);
    }

    // Best (largest) effective discount per dish when several promotions run at once.
    public static Dictionary<int, decimal> BestPercents(IEnumerable<PromotionPriceRow> rows)
        => rows.GroupBy(row => row.MenuItemId).ToDictionary(group => group.Key,
            group => group.Max(row => EffectivePercent(row.BasePrice, row.DiscountPercent, row.PromoPrice)));

    // Same choice as BestPercents, also keeping the chosen promotion's end date (for "Ends Sun" labels).
    public static Dictionary<int, BestPromotion> Best(IEnumerable<PromotionPriceRow> rows)
        => rows.GroupBy(row => row.MenuItemId).ToDictionary(group => group.Key, group => group
            .Select(row => new BestPromotion(EffectivePercent(row.BasePrice, row.DiscountPercent, row.PromoPrice), row.EndDate))
            .OrderByDescending(best => best.Percent).ThenBy(best => best.EndDate).First());

    // Customer label for the last day, e.g. "Ends today", "Ends tomorrow", "Ends Sun", "Ends 20 Nov".
    public static string EndsLabel(DateTime endDate, DateTime now)
    {
        int days = (endDate.Date - now.Date).Days;
        return days switch
        {
            <= 0 => "Ends today",
            1 => "Ends tomorrow",
            < 7 => "Ends " + endDate.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture),
            _ => "Until " + endDate.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}

public sealed record PromotionPriceRow(int MenuItemId, decimal BasePrice, decimal DiscountPercent, decimal? PromoPrice,
    DateTime? EndDate = null);

// The promotion that applies to one dish: its % off today's price and its last day.
public sealed record BestPromotion(decimal Percent, DateTime? EndDate);
