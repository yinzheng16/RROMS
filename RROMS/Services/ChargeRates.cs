using System.Globalization;
using RROMS.Models;

namespace RROMS.Services
{
    // Owner: Member 4 (US32 bill, US35 receipt)
    // Tax and service-charge percentages for the staff bill and the PDF receipt.
    // Display only: every amount still comes from the saved orders and bills.
    // Member 3 saves Order.TaxRatePercent / ServiceChargePercent when an order
    // is placed. A % is shown only when every chargeable (non-cancelled) order
    // on the bill saved the same rate. null and 0 are different:
    //   null = no reliable rate (older order, or orders with mixed rates)
    //          -> name only; never treated as 0% and never worked back from amounts;
    //   0    = a real saved 0% (e.g. takeaway service charge) -> "(0%)".
    // Callers pass only the orders actually charged on the bill.
    public static class ChargeRates
    {
        public static (decimal? TaxRate, decimal? ServiceChargeRate) SharedSaved(
            IEnumerable<Order> orders)
        {
            var list = orders.ToList();

            return (
                Shared(list.Select(o => o.TaxRatePercent)),
                Shared(list.Select(o => o.ServiceChargePercent)));
        }

        // "Tax (6%)", "Service Charge (0%)"; only the name when the rate is null.
        // Same format as Member 3's order pages ("0.##").
        public static string Label(string name, decimal? rate) =>
            rate is decimal r
                ? $"{name} ({r.ToString("0.##", CultureInfo.InvariantCulture)}%)"
                : name;

        private static decimal? Shared(IEnumerable<decimal?> rates)
        {
            var list = rates.ToList();

            if (list.Count == 0 || list.Any(r => r is null))
                return null;

            return list.Distinct().Count() == 1 ? list[0] : null;
        }
    }
}
