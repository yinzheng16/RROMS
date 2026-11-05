using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer;

// Owner: Member 3. What the "Pay now" panel (_TakeawayPay.cshtml) needs for one order.
// Takeaway: Member 4's GetTakeawayPayStatusAsync (US33) decides the button.
// Dine-in: no button; the table's combined bill is paid on Member 4's Pay page.
public sealed record TakeawayPayView(
    int OrderId,
    OrderType OrderType,
    OrderStatus OrderStatus,
    decimal Total,
    TakeawayPayStatus? Status,     // null: dine-in, or Stripe could not be checked
    bool OnlineAvailable,
    decimal MinimumAmount,
    bool Dark = false)             // true on the dark order-confirmation panel
{
    public const string ViewDataKey = "TakeawayPay";

    public static async Task<TakeawayPayView> LoadAsync(IOnlinePaymentService online, ILogger logger,
        int orderId, OrderType orderType, OrderStatus orderStatus, decimal total, bool dark = false)
    {
        TakeawayPayStatus? status = null;
        if (orderType == OrderType.Takeaway)
        {
            try
            {
                status = await online.GetTakeawayPayStatusAsync(orderId);
            }
            catch (Exception ex) when (ex is Stripe.StripeException or HttpRequestException)
            {
                // Stripe unreachable: the order page still works; the panel just isn't shown.
                logger.LogWarning(ex, "Pay now: could not check the payment of order {OrderId}.", orderId);
            }
        }
        return new(orderId, orderType, orderStatus, total, status,
            online.IsOnlinePaymentAvailable, online.MinimumOnlineAmount, dark);
    }
}
