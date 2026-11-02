using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 4 (US55 - Refund a payment)
    // Full refunds only (partial refunds are future work).
    // - Only a Paid payment can be refunded; a conditional update
    //   (Status = Paid -> Refunded) inside the bill payment lock means two
    //   admins clicking at the same time can never refund it twice.
    // - Online: Stripe refund first (idempotency key rroms-refund-{id});
    //   if Stripe refuses, nothing is saved.
    // - The bill stays Paid: Member 2's session closing, Member 3's order
    //   cancellation and the staff billing list keep working unchanged.
    //   "Refunded" lives on the payment only.
    public class PaymentRefundService : IPaymentRefundService
    {
        private readonly IRepository<Payment> _payments;
        private readonly IBillingTransactionRepository _billingTransaction;
        private readonly IStripeCheckoutGateway _stripe;
        private readonly INotificationService _notifications;
        private readonly ILogger<PaymentRefundService> _logger;

        public PaymentRefundService(
            IRepository<Payment> payments,
            IBillingTransactionRepository billingTransaction,
            IStripeCheckoutGateway stripe,
            INotificationService notifications,
            ILogger<PaymentRefundService> logger)
        {
            _payments = payments;
            _billingTransaction = billingTransaction;
            _stripe = stripe;
            _notifications = notifications;
            _logger = logger;
        }

        public async Task<RefundOutcome> RefundAsync(
            int paymentId,
            string adminUserId,
            string reason,
            string? note,
            bool moneyReturned)
        {
            if (paymentId <= 0)
                throw new ArgumentException("Invalid payment.");

            if (string.IsNullOrWhiteSpace(adminUserId))
                throw new ArgumentException("Admin user is required.");

            string pickedReason = RefundReasons.All
                .FirstOrDefault(r => string.Equals(r, reason?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Choose a reason for the refund.");

            string? cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

            if (cleanNote != null && cleanNote.Length > RefundReasons.NoteMaxLength)
                throw new ArgumentException(
                    $"The note can be at most {RefundReasons.NoteMaxLength} characters.");

            if (pickedReason == RefundReasons.Other && cleanNote == null)
                throw new ArgumentException("Add a note when the reason is Other.");

            string savedReason = cleanNote == null ? pickedReason : $"{pickedReason} · {cleanNote}";

            var payment = await _payments.Query()
                .AsNoTracking()
                .Where(p => p.Id == paymentId)
                .Select(p => new { p.Id, p.BillId, p.Status, p.Method, p.Amount, p.TransactionRef })
                .SingleOrDefaultAsync()
                ?? throw new InvalidOperationException("Payment does not exist.");

            CheckStillPaid(payment.Status);

            bool online = payment.Method == PaymentMethod.Online;

            if (!online && !moneyReturned)
                throw new ArgumentException(
                    "Return the money to the customer first, then tick the box.");

            if (online && string.IsNullOrWhiteSpace(payment.TransactionRef))
                throw new InvalidOperationException(
                    "This online payment has no Stripe reference, so it can't be refunded here.");

            if (online && !_stripe.IsEnabled)
                throw new InvalidOperationException(
                    "Online refunds are not available right now (no Stripe test key is set).");

            async Task<bool> RunRefund()
            {
                // Check again inside the lock: someone may have refunded it meanwhile.
                var status = await _payments.Query()
                    .AsNoTracking()
                    .Where(p => p.Id == paymentId)
                    .Select(p => p.Status)
                    .SingleAsync();

                CheckStillPaid(status);

                if (online)
                    await RefundOnStripeAsync(paymentId, payment.TransactionRef!, payment.Amount,
                        pickedReason == RefundReasons.ChargedTwice);

                DateTime now = DateTime.Now;   // team rule: local server time

                int updated = await _payments.Query()
                    .Where(p => p.Id == paymentId && p.Status == PaymentStatus.Paid)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.Status, PaymentStatus.Refunded)
                        .SetProperty(p => p.RefundedAt, (DateTime?)now)
                        .SetProperty(p => p.RefundedByUserId, adminUserId)
                        .SetProperty(p => p.RefundReason, savedReason));

                if (updated != 1)
                    throw new InvalidOperationException("This payment has already been refunded.");

                return true;
            }

            // Same lock as recording a payment for this bill.
            // Older order-based payments (US31) have no bill; the conditional update protects them.
            if (payment.BillId is int billId)
                await _billingTransaction.ExecuteForBillPaymentAsync(billId, RunRefund);
            else
                await RunRefund();

            await NotifyCustomerAsync(paymentId, payment.Amount, online, pickedReason);

            return new RefundOutcome(paymentId, payment.Amount, payment.Method);
        }

        private static void CheckStillPaid(PaymentStatus status)
        {
            if (status == PaymentStatus.Refunded)
                throw new InvalidOperationException("This payment has already been refunded.");

            if (status != PaymentStatus.Paid)
                throw new InvalidOperationException("Only paid payments can be refunded.");
        }

        private async Task RefundOnStripeAsync(int paymentId, string checkoutSessionId, decimal amount, bool duplicate)
        {
            try
            {
                // TransactionRef holds the Checkout Session id (US33); the refund needs its PaymentIntent.
                var session = await _stripe.GetSessionAsync(checkoutSessionId.Trim());

                if (!session.IsPaid || string.IsNullOrWhiteSpace(session.PaymentIntentId))
                    throw new InvalidOperationException(
                        "Stripe does not show this payment as paid, so it can't be refunded.");

                long sen = decimal.ToInt64(amount * 100m);

                var refund = await _stripe.RefundAsync(new StripeRefundRequest(
                    paymentId, session.PaymentIntentId, sen, duplicate));

                if (!refund.IsAccepted)
                    throw new InvalidOperationException(
                        $"Stripe did not accept the refund (status: {refund.Status}). Nothing was changed.");
            }
            catch (Stripe.StripeException ex)
            {
                _logger.LogWarning(ex, "US55: Stripe refund failed for payment {PaymentId}", paymentId);
                throw new InvalidOperationException(
                    $"Stripe could not refund this payment: {ex.StripeError?.Message ?? ex.Message} Nothing was changed.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "US55: Stripe not reachable for payment {PaymentId}", paymentId);
                throw new InvalidOperationException(
                    "Stripe could not be reached. Check the connection and try again. Nothing was changed.");
            }
        }

        // The customer of the payment: dine-in = the dining session's customer,
        // takeaway = who placed the order, older payments = the order's customer.
        // A failed notification never undoes the refund.
        private async Task NotifyCustomerAsync(int paymentId, decimal amount, bool online, string reason)
        {
            try
            {
                var target = await _payments.Query()
                    .AsNoTracking()
                    .Where(p => p.Id == paymentId)
                    .Select(p => new
                    {
                        CustomerId = p.Bill != null
                            ? (p.Bill.DiningSession != null
                                ? p.Bill.DiningSession.CustomerId
                                : p.Bill.Orders.OrderBy(o => o.Id).Select(o => o.UserId).FirstOrDefault())
                            : p.Order!.UserId,
                        p.BillId
                    })
                    .SingleOrDefaultAsync();

                if (target == null || string.IsNullOrWhiteSpace(target.CustomerId))
                    return;

                string receiptNo = PaymentReceipt.FormatReceiptNo(paymentId);
                string billPart = target.BillId is int b ? $" (bill YB-{b:D6})" : "";
                string how = online ? " It goes back to your card through Stripe." : "";

                await _notifications.SendAsync(
                    target.CustomerId,
                    "Payment refunded",
                    $"Your payment {receiptNo}{billPart} of RM {amount:N2} has been refunded. Reason: {reason}.{how}",
                    NotificationType.Payment);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "US55: refund notification failed for payment {PaymentId}", paymentId);
            }
        }
    }
}
