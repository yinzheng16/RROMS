namespace RROMS.Services.Interfaces
{
    // US33 (Member 4): The only place that talks to Stripe.
    // OnlinePaymentService depends on this interface, so unit tests can use a
    // fake gateway without calling Stripe.
    public interface IStripeCheckoutGateway
    {
        // False when no Stripe test key is set (online payment switched off).
        bool IsEnabled { get; }

        // Create a Stripe Checkout Session (Stripe's own hosted payment page).
        Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request);

        // Read the current state of a Checkout Session from Stripe.
        Task<StripeCheckoutSession> GetSessionAsync(string sessionId);

        // Close an unfinished Checkout Session so it can no longer be paid.
        // If it was completed in the meantime, the completed session is returned.
        Task<StripeCheckoutSession> ExpireSessionAsync(string sessionId);

        // US55: Refund a paid Checkout payment (full amount) back to the customer's card.
        // The same payment can never be refunded twice (idempotency key per payment).
        Task<StripeRefundResult> RefundAsync(StripeRefundRequest request);
    }

    // US55: What we ask Stripe to refund. Amount is in sen (RM 12.50 = 1250).
    public sealed record StripeRefundRequest(
        int PaymentId,
        string PaymentIntentId,
        long AmountInSen,
        bool Duplicate
    );

    // Status: "succeeded", "pending", "failed", "canceled" or "requires_action".
    public sealed record StripeRefundResult(string Id, string Status, long Amount)
    {
        // Pending = Stripe accepted it and finishes it on its side.
        public bool IsAccepted => Status is "succeeded" or "pending";
    }

    // What we ask Stripe to charge. Amount is in sen (RM 12.50 = 1250).
    public sealed record StripeCheckoutRequest(
        int PaymentId,
        int BillId,
        long AmountInSen,
        string Currency,
        string Description,
        string SuccessUrl,
        string CancelUrl,
        DateTime ExpiresAtUtc
    );

    // The parts of a Stripe Checkout Session that billing needs.
    // Status: "open", "complete" or "expired".
    // PaymentStatus: "paid", "unpaid" or "no_payment_required".
    public sealed record StripeCheckoutSession(
        string Id,
        string? Url,
        string Status,
        string PaymentStatus,
        long? AmountTotal,
        string? Currency,
        string? PaymentIntentId,
        IReadOnlyDictionary<string, string> Metadata
    )
    {
        public bool IsPaid => Status == "complete" && PaymentStatus == "paid";
        public bool IsExpired => Status == "expired";
        public bool IsOpen => Status == "open";
    }
}
