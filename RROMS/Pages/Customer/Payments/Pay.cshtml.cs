// Owner: Member 4 — US33 Online payment: the customer pay page.
// Lists what the signed-in customer can pay online and starts Stripe Checkout.
// Member 3's "Pay now" button on takeaway orders posts to OnPostTakeawayAsync.
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Payments;

// The /Customer folder already requires CustomerOnly (Program.cs);
// the attribute repeats it so the page stays protected if moved.
[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PayModel(IOnlinePaymentService online, ILogger<PayModel> logger) : PageModel
{
    private readonly IOnlinePaymentService _online = online;
    private readonly ILogger<PayModel> _logger = logger;

    public static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-MY");

    public IReadOnlyList<PayableItem> Items { get; private set; } = Array.Empty<PayableItem>();

    public bool OnlineAvailable => _online.IsOnlinePaymentAvailable;

    public decimal MinimumAmount => _online.MinimumOnlineAmount;

    // ?orderId=123 highlights that takeaway order (e.g. after a failed attempt).
    [BindProperty(SupportsGet = true)]
    public int? OrderId { get; set; }

    [TempData]
    public string? PayError { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // The customer always comes from the login, never from the query string.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Challenge();

        try
        {
            Items = await _online.GetPayableItemsAsync(userId);
        }
        catch (Exception ex) when (ex is Stripe.StripeException or HttpRequestException)
        {
            // Stripe unreachable while checking an unfinished payment.
            _logger.LogWarning(ex, "US33: could not check online payments with Stripe.");
            PayError ??= "We couldn't check your online payments right now. Please refresh in a moment.";
        }

        return Page();
    }

    // Takeaway order: create/confirm its bill, then go straight to Stripe.
    public Task<IActionResult> OnPostTakeawayAsync(int orderId)
        => StartAsync(userId => _online.StartTakeawayCheckoutAsync(orderId, userId, CheckoutUrls()), orderId);

    // Confirmed bill (dine-in, or a takeaway bill already created).
    public Task<IActionResult> OnPostBillAsync(int billId)
        => StartAsync(userId => _online.StartBillCheckoutAsync(billId, userId, CheckoutUrls()), null);

    private async Task<IActionResult> StartAsync(Func<string, Task<string>> start, int? orderId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Challenge();

        try
        {
            string stripeUrl = await start(userId);
            return Redirect(stripeUrl);   // Stripe's hosted payment page
        }
        catch (InvalidOperationException ex)
        {
            PayError = ex.Message;
        }
        catch (Exception ex) when (ex is Stripe.StripeException or HttpRequestException)
        {
            _logger.LogError(ex, "US33: Stripe Checkout could not be started.");
            PayError = "We couldn't open the payment page. Nothing was charged. Please try again, or pay at the counter.";
        }

        return RedirectToPage(new { orderId });
    }

    // Where Stripe sends the customer back. Stripe fills in the session id;
    // OnlinePaymentService fills in the payment id.
    private OnlineCheckoutUrls CheckoutUrls()
    {
        const string sid = "__SESSION__";
        const string pid = "__PAYMENT__";

        string success = Url.Page("/Customer/Payments/PayResult", null,
                new { session_id = sid }, Request.Scheme)!
            .Replace(sid, OnlineCheckoutUrls.SessionIdToken, StringComparison.Ordinal);

        string cancel = Url.Page("/Customer/Payments/PayResult", null,
                new { payment = pid, cancelled = true }, Request.Scheme)!
            .Replace(pid, OnlineCheckoutUrls.PaymentIdToken, StringComparison.Ordinal);

        return new OnlineCheckoutUrls(success, cancel);
    }
}
