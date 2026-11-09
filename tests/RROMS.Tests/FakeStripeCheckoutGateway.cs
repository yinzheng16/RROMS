using RROMS.Services.Interfaces;

namespace RROMS.Tests;

// QA01 (Member 4): test double for IStripeCheckoutGateway.
// OnlinePaymentService and PaymentRefundService only reach Stripe through
// this interface, so the tests decide what "Stripe" answers without any
// network call. Every request is recorded so the tests can check it.
public sealed class FakeStripeCheckoutGateway : IStripeCheckoutGateway
{
    public bool IsEnabled { get; set; } = true;

    // What Stripe "knows": Checkout Sessions by id.
    public Dictionary<string, StripeCheckoutSession> Sessions { get; } = new();

    public List<StripeCheckoutRequest> CreatedRequests { get; } = new();
    public List<string> ExpiredSessionIds { get; } = new();
    public List<StripeRefundRequest> RefundRequests { get; } = new();

    // Status returned for a refund: "succeeded", "failed", ...
    public string RefundStatus { get; set; } = "succeeded";

    public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request)
    {
        CreatedRequests.Add(request);

        string id = "cs_test_" + Guid.NewGuid().ToString("N")[..16];

        var session = new StripeCheckoutSession(
            id,
            "https://checkout.stripe.test/" + id,
            "open",
            "unpaid",
            request.AmountInSen,
            request.Currency,
            null,
            new Dictionary<string, string>
            {
                ["rroms_payment_id"] = request.PaymentId.ToString()
            });

        Sessions[id] = session;
        return Task.FromResult(session);
    }

    public Task<StripeCheckoutSession> GetSessionAsync(string sessionId) =>
        Task.FromResult(Sessions[sessionId]);

    public Task<StripeCheckoutSession> ExpireSessionAsync(string sessionId)
    {
        ExpiredSessionIds.Add(sessionId);

        var session = Sessions[sessionId];

        if (session.IsOpen)
        {
            session = session with { Status = "expired" };
            Sessions[sessionId] = session;
        }

        return Task.FromResult(session);
    }

    public Task<StripeRefundResult> RefundAsync(StripeRefundRequest request)
    {
        RefundRequests.Add(request);

        return Task.FromResult(new StripeRefundResult(
            "re_test_" + request.PaymentId, RefundStatus, request.AmountInSen));
    }

    // ---- helpers to put a session in a given state ----

    public static StripeCheckoutSession Paid(
        string id, long amountInSen, int paymentId, string currency = "myr") =>
        new(id, null, "complete", "paid", amountInSen, currency, "pi_test_" + paymentId,
            new Dictionary<string, string> { ["rroms_payment_id"] = paymentId.ToString() });

    public static StripeCheckoutSession Open(string id, long amountInSen, int paymentId) =>
        new(id, "https://checkout.stripe.test/" + id, "open", "unpaid", amountInSen, "myr", null,
            new Dictionary<string, string> { ["rroms_payment_id"] = paymentId.ToString() });

    public static StripeCheckoutSession Expired(string id, long amountInSen, int paymentId) =>
        new(id, null, "expired", "unpaid", amountInSen, "myr", null,
            new Dictionary<string, string> { ["rroms_payment_id"] = paymentId.ToString() });
}
