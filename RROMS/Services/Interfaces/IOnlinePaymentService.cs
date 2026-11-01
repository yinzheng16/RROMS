using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // US33 (Member 4): Customer pays a bill online with Stripe (test mode).
    // - Dine-in: the combined bill must already be confirmed by staff.
    // - Takeaway: the customer can pay when ordering; the takeaway bill is
    //   created and confirmed automatically. If that payment fails or expires
    //   and nothing is paid, the bill goes back to Draft (order can still be
    //   cancelled or paid at the counter).
    // A bill is Paid only when the full amount is paid; a duplicate return
    // from Stripe never records a second payment.
    public interface IOnlinePaymentService
    {
        // False when Stripe is not set up on this computer: hide "Pay online".
        bool IsOnlinePaymentAvailable { get; }

        // Stripe's minimum charge in MYR; smaller bills are paid at the counter.
        decimal MinimumOnlineAmount { get; }

        // Takeaway: create/confirm the order's bill, then start Stripe Checkout.
        // Returns the Stripe page URL to redirect the customer to.
        Task<string> StartTakeawayCheckoutAsync(
            int orderId, string customerId, OnlineCheckoutUrls urls);

        // Any confirmed bill of this customer (dine-in or takeaway).
        // Returns the Stripe page URL to redirect the customer to.
        Task<string> StartBillCheckoutAsync(
            int billId, string customerId, OnlineCheckoutUrls urls);

        // Success page: ask Stripe whether this session was paid and record it.
        // customerId: only the bill's customer may look at the result.
        Task<OnlinePaymentOutcome> CompleteCheckoutAsync(
            string sessionId, string customerId);

        // Cancel page (customer pressed back on Stripe): close the session and
        // mark the attempt Failed so the customer can try again.
        Task<OnlinePaymentOutcome> CancelCheckoutAsync(
            int paymentId, string customerId);

        // Called by Member 3's order pages to decide which button to show
        // for a takeaway order (Pay now / Continue payment / Paid / pay at
        // the counter). Includes the IsOnlinePaymentAvailable check.
        Task<TakeawayPayStatus> GetTakeawayPayStatusAsync(int orderId);

        // Pay page: everything this customer can pay online now —
        // confirmed dine-in bills and unpaid takeaway orders. Unfinished
        // online payments are checked with Stripe first.
        Task<IReadOnlyList<PayableItem>> GetPayableItemsAsync(string customerId);

        // Staff bill page: is an online payment running for this bill, and
        // was a takeaway bill confirmed by its customer for online payment?
        // Database only (call ReconcileBillAsync first for fresh data).
        Task<OnlineBillInfo> GetOnlineBillInfoAsync(int billId);

        // Staff bill page: the customer will pay at the counter instead.
        // Stripe is checked first: if the customer already paid, the bill
        // becomes Paid (returns Paid). Otherwise the Stripe session is closed
        // and the attempt marked Failed; the bill stays Confirmed so staff
        // can record the counter payment straight away.
        Task<OnlinePaymentState> CancelForCounterPaymentAsync(int billId);

        // Staff billing list: check every bill that still has an unfinished
        // online payment (normally only a few).
        Task ReconcileAllPendingAsync();

        // Check every unfinished online payment of a bill with Stripe
        // (paid / expired / timed out). Safe to call on every page load;
        // this replaces a webhook on localhost.
        Task ReconcileBillAsync(int billId);
    }

    // SuccessUrl must contain {CHECKOUT_SESSION_ID} (Stripe fills it in).
    // CancelUrl must contain {PAYMENT_ID} (filled in by OnlinePaymentService).
    public sealed record OnlineCheckoutUrls(string SuccessUrl, string CancelUrl)
    {
        public const string PaymentIdToken = "{PAYMENT_ID}";
        public const string SessionIdToken = "{CHECKOUT_SESSION_ID}";
    }

    // For Member 3's "Pay now" button on takeaway orders.
    public enum TakeawayPayStatus
    {
        CanPay,         // show "Pay now"
        InProgress,     // show "Continue payment" (same form)
        Paid,           // show "Paid"
        PayAtCounter    // cancelled, under RM 2.00, or Stripe not set up
    }

    // Staff bill page. Not a database entity.
    public sealed record OnlineBillInfo(
        bool ConfirmedByCustomer,
        int? PendingPaymentId,
        DateTime? StartedAt,
        DateTime? LockedUntil
    )
    {
        public bool InProgress => PendingPaymentId != null;
    }

    public enum PayableKind
    {
        DineIn,
        Takeaway
    }

    // One row on the customer pay page. Not a database entity.
    public sealed record PayableItem(
        PayableKind Kind,
        int? BillId,
        int? OrderId,
        string Reference,           // table number (dine-in) or order number (takeaway)
        decimal Amount,
        int OrderCount,
        DateTime? ConfirmedAt,
        DateTime CreatedAt,
        OrderStatus? KitchenStatus, // takeaway only
        TakeawayPayStatus PayStatus,
        DateTime? InProgressSince
    )
    {
        public string? BillNo => BillId is int id ? $"YB-{id:D6}" : null;
    }

    public enum OnlinePaymentState
    {
        Paid,
        StillPending,
        Failed,
        NotFound
    }

    public sealed record OnlinePaymentOutcome(
        OnlinePaymentState State,
        int? BillId,
        int? PaymentId,
        decimal? Amount,
        DateTime? PaidAt,
        int? TakeawayOrderId = null,
        string? TakeawayOrderNo = null
    )
    {
        public string? BillNo => BillId is int id ? $"YB-{id:D6}" : null;
    }
}
