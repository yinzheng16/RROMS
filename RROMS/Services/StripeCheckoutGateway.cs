using Microsoft.Extensions.Options;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // US33 (Member 4): Stripe Checkout in test mode (Stripe.net).
    // Customers pay on Stripe's hosted page, so card details never reach RROMS.
    public class StripeCheckoutGateway : IStripeCheckoutGateway
    {
        private readonly Stripe.Checkout.SessionService? _sessions;

        // US55: refunds of paid Checkout payments.
        private readonly Stripe.RefundService? _refunds;

        public StripeCheckoutGateway(IOptions<StripeSettings> options)
        {
            var settings = options.Value;

            // No (test) key = online payment off; nothing is sent to Stripe.
            if (settings.IsEnabled)
            {
                var client = new Stripe.StripeClient(settings.SecretKey);
                _sessions = new Stripe.Checkout.SessionService(client);
                _refunds = new Stripe.RefundService(client);
            }
        }

        public bool IsEnabled => _sessions != null;

        public async Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request)
        {
            var sessions = RequireEnabled();

            var options = new Stripe.Checkout.SessionCreateOptions
            {
                Mode = "payment",
                // Payment methods come from the Stripe Dashboard settings
                // (cards are on by default in test mode).
                LineItems = new List<Stripe.Checkout.SessionLineItemOptions>
                {
                    new()
                    {
                        Quantity = 1,
                        PriceData = new Stripe.Checkout.SessionLineItemPriceDataOptions
                        {
                            Currency = request.Currency,
                            UnitAmount = request.AmountInSen,
                            ProductData = new Stripe.Checkout.SessionLineItemPriceDataProductDataOptions
                            {
                                Name = request.Description
                            }
                        }
                    }
                },
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                ExpiresAt = request.ExpiresAtUtc,
                ClientReferenceId = $"bill-{request.BillId}",
                Metadata = new Dictionary<string, string>
                {
                    ["rroms_bill_id"] = request.BillId.ToString(),
                    ["rroms_payment_id"] = request.PaymentId.ToString()
                }
            };

            // Idempotency key: retrying the same payment attempt can never
            // create a second Stripe session.
            var requestOptions = new Stripe.RequestOptions
            {
                IdempotencyKey = $"rroms-payment-{request.PaymentId}"
            };

            var session = await sessions.CreateAsync(options, requestOptions);
            return Map(session);
        }

        public async Task<StripeCheckoutSession> GetSessionAsync(string sessionId)
        {
            var sessions = RequireEnabled();
            var session = await sessions.GetAsync(sessionId);
            return Map(session);
        }

        public async Task<StripeCheckoutSession> ExpireSessionAsync(string sessionId)
        {
            var sessions = RequireEnabled();

            try
            {
                var session = await sessions.ExpireAsync(sessionId);
                return Map(session);
            }
            catch (Stripe.StripeException)
            {
                // Only open sessions can be expired. If the customer finished
                // paying (or it already expired), return its real state.
                var session = await sessions.GetAsync(sessionId);
                return Map(session);
            }
        }

        public async Task<StripeRefundResult> RefundAsync(StripeRefundRequest request)
        {
            RequireEnabled();

            var options = new Stripe.RefundCreateOptions
            {
                PaymentIntent = request.PaymentIntentId,
                Amount = request.AmountInSen,
                Reason = request.Duplicate ? "duplicate" : "requested_by_customer",
                Metadata = new Dictionary<string, string>
                {
                    ["rroms_payment_id"] = request.PaymentId.ToString()
                }
            };

            // Clicking Refund again for the same payment returns the first
            // refund instead of refunding a second time.
            var requestOptions = new Stripe.RequestOptions
            {
                IdempotencyKey = $"rroms-refund-{request.PaymentId}"
            };

            var refund = await _refunds!.CreateAsync(options, requestOptions);
            return new StripeRefundResult(refund.Id, refund.Status ?? "", refund.Amount);
        }

        private Stripe.Checkout.SessionService RequireEnabled()
            => _sessions ?? throw new InvalidOperationException(
                "Online payment is not available right now. Please pay at the counter.");

        private static StripeCheckoutSession Map(Stripe.Checkout.Session s) => new(
            s.Id,
            s.Url,
            s.Status ?? "",
            s.PaymentStatus ?? "",
            s.AmountTotal,
            s.Currency,
            s.PaymentIntentId,
            s.Metadata ?? new Dictionary<string, string>());
    }
}
