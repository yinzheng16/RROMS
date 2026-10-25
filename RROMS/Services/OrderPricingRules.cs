using RROMS.Services.Interfaces;

namespace RROMS.Services;

// Owner: Member 3. Shared calculation point for US24 and the future billing module.
// Both charges use the discounted food/beverage subtotal as their base.
// Rates come from RestaurantSetting. No tax rate or takeaway exemption is invented.
public static class OrderPricingRules
{
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static CartTotals Calculate(decimal subtotal, decimal taxRate, decimal serviceRate)
    {
        if (subtotal < 0 || taxRate is < 0 or > 100 || serviceRate is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(subtotal));
        subtotal = Money(subtotal);
        decimal tax = Money(subtotal * taxRate / 100m);
        decimal service = Money(subtotal * serviceRate / 100m);
        return new(subtotal, taxRate, tax, serviceRate, service, subtotal + tax + service);
    }

    public static decimal DiscountedPrice(decimal price, decimal discountPercent)
        => Money(price * (100m - Math.Clamp(discountPercent, 0m, 100m)) / 100m);

    public static string? NormalizeNote(string? note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim().Replace("\r\n", "\n");
}
