using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Service charge and tax worked out for one subtotal (all amounts in RM, 2 decimals).
    public record BillCharges(
        decimal Subtotal,
        decimal ServiceChargePercent,   // 0 when not charged (e.g. takeaway)
        decimal ServiceCharge,
        decimal TaxRatePercent,
        decimal Tax,
        decimal Total);

    public interface IReportService
    {
        Task<RestaurantSetting> GetRestaurantSettingsAsync();
        Task UpdateRestaurantSettingsAsync(RestaurantSetting settings);

        // US49: the ONE place the bill maths lives. Orders (Member 3) and bills (Member 4)
        // both call this instead of writing their own formula.
        //   service = subtotal x service%      (0 for takeaway unless the admin turned it on)
        //   tax     = (subtotal + service) x tax%
        //   total   = subtotal + service + tax
        Task<BillCharges> CalculateChargesAsync(decimal subtotal, bool isTakeaway);

        // Same, with settings you already loaded (no extra database call).
        BillCharges CalculateCharges(RestaurantSetting settings, decimal subtotal, bool isTakeaway);
    }
}
