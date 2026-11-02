// Owner: Member 4 — US33 Online payment: the page Stripe sends the customer back to.
// Success: ?session_id=cs_test_...  -> ask Stripe and record the payment (only once).
// Cancel:  ?payment=12&cancelled=true -> close the Stripe session, nothing charged.
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Payments;

[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PayResultModel(IOnlinePaymentService online, ILogger<PayResultModel> logger) : PageModel
{
    private readonly IOnlinePaymentService _online = online;
    private readonly ILogger<PayResultModel> _logger = logger;

    public static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-MY");

    public OnlinePaymentOutcome? Outcome { get; private set; }

    public bool CheckFailed { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = "session_id")] string? sessionId,
        int? payment,
        bool cancelled)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Challenge();

        try
        {
            if (cancelled && payment is int paymentId)
                Outcome = await _online.CancelCheckoutAsync(paymentId, userId);
            else if (!string.IsNullOrWhiteSpace(sessionId))
                Outcome = await _online.CompleteCheckoutAsync(sessionId, userId);
        }
        catch (Exception ex) when (ex is Stripe.StripeException or HttpRequestException)
        {
            // Nothing is lost: the payment stays Pending and is checked again
            // on the next visit (pay page, staff bill page).
            _logger.LogWarning(ex, "US33: could not check the payment with Stripe.");
            CheckFailed = true;
        }

        return Page();
    }
}
