using RROMS.Models;
using RROMS.Services;

namespace RROMS.Tests;

// QA01 (Member 4): rates shown on the bill and receipt (PR #160).
// A % is shown only when every charged order saved the same rate;
// null (unknown) and 0 (a real 0%) must never be mixed up.
public class ChargeRatesTests
{
    private static Order OrderWith(decimal? tax, decimal? service) =>
        new() { TaxRatePercent = tax, ServiceChargePercent = service };

    [Fact]
    public void SameSavedRates_AreShown()
    {
        var (tax, service) = ChargeRates.SharedSaved(new[]
        {
            OrderWith(6m, 10m),
            OrderWith(6m, 10m)
        });

        Assert.Equal(6m, tax);
        Assert.Equal(10m, service);
    }

    [Fact]
    public void DifferentRates_ShowNoPercentage()
    {
        var (tax, service) = ChargeRates.SharedSaved(new[]
        {
            OrderWith(6m, 10m),
            OrderWith(8m, 10m)
        });

        Assert.Null(tax);
        Assert.Equal(10m, service);
    }

    [Fact]
    public void OlderOrderWithoutSavedRate_ShowsNoPercentage()
    {
        var (tax, service) = ChargeRates.SharedSaved(new[]
        {
            OrderWith(6m, 10m),
            OrderWith(null, null)
        });

        Assert.Null(tax);
        Assert.Null(service);
    }

    [Fact]
    public void ZeroIsARealRate_AndLabelsAreFormatted()
    {
        var (_, service) = ChargeRates.SharedSaved(new[] { OrderWith(6m, 0m) });

        Assert.Equal(0m, service);
        Assert.Equal("Tax (6%)", ChargeRates.Label("Tax", 6m));
        Assert.Equal("Service charge (0%)", ChargeRates.Label("Service charge", 0m));
        Assert.Equal("Tax", ChargeRates.Label("Tax", null));
    }
}
