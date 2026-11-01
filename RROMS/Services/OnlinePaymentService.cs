using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // US33 (Member 4): Online payment of a bill with Stripe Checkout (test mode).
    //
    // How one online payment works:
    // 1. Start: inside the bill payment lock, a Pending payment (Method Online)
    //    is saved, then a Stripe Checkout Session is created and its id is kept
    //    in Payment.TransactionRef. The customer is sent to Stripe's page.
    // 2. Finish: the success page (or a page load later) asks Stripe for the
    //    session. Paid -> payment Pending -> Paid and bill Confirmed -> Paid,
    //    both by conditional update, so a second request changes nothing.
    //    Expired / cancelled / older than CheckoutMinutes -> payment Failed.
    // 3. Takeaway only: the bill is confirmed automatically when the customer
    //    chooses to pay online. If that attempt fails and nothing is paid,
    //    the bill goes back to Draft.
    //
    // Locks: the bill payment lock and the takeaway order lock are never
    // nested; each runs in its own transaction.
    public class OnlinePaymentService : IOnlinePaymentService
    {
        private const decimal StripeMinimumMyr = 2.00m;

        private readonly IRepository<Payment> _payments;
        private readonly IRepository<Order> _orders;
        private readonly IRepository<Bill> _bills;
        private readonly IBillingService _billing;
        private readonly IBillingTransactionRepository _billingTransaction;
        private readonly IStripeCheckoutGateway _stripe;
        private readonly StripeSettings _settings;
        private readonly ILogger<OnlinePaymentService> _logger;

        public OnlinePaymentService(
            IRepository<Payment> payments,
            IRepository<Order> orders,
            IRepository<Bill> bills,
            IBillingService billing,
            IBillingTransactionRepository billingTransaction,
            IStripeCheckoutGateway stripe,
            IOptions<StripeSettings> settings,
            ILogger<OnlinePaymentService> logger)
        {
            _payments = payments;
            _orders = orders;
            _bills = bills;
            _billing = billing;
            _billingTransaction = billingTransaction;
            _stripe = stripe;
            _settings = settings.Value;
            _logger = logger;
        }

        public bool IsOnlinePaymentAvailable => _stripe.IsEnabled;

        public decimal MinimumOnlineAmount => StripeMinimumMyr;

        private TimeSpan CheckoutTime =>
            TimeSpan.FromMinutes(Math.Max(1, _settings.CheckoutMinutes));

        // ---------------------------------------------------------------
        // Start
        // ---------------------------------------------------------------

        public async Task<string> StartTakeawayCheckoutAsync(
            int orderId, string customerId, OnlineCheckoutUrls urls)
        {
            RequireAvailable();
            ValidateCustomer(customerId);

            if (orderId <= 0)
                throw new InvalidOperationException("Order not found.");

            var order = await _orders.Query()
                .AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new { o.UserId, o.OrderType, o.Status, o.BillId })
                .FirstOrDefaultAsync();

            // Same message for "not yours" and "does not exist".
            if (order == null || order.UserId != customerId)
                throw new InvalidOperationException("Order not found.");

            if (order.OrderType != OrderType.Takeaway)
                throw new InvalidOperationException(
                    "Dine-in orders are paid on the table's combined bill after staff confirm it.");

            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException("This order was cancelled.");

            // Create (or reuse) the takeaway bill. Runs in its own lock.
            var bill = await _billing.CreateTakeawayBillAsync(orderId);

            if (bill.Status == BillStatus.Paid)
                throw new InvalidOperationException("This order has already been paid.");

            if (bill.Status == BillStatus.Draft)
                await ConfirmTakeawayBillForOnlinePaymentAsync(orderId, bill.Id, customerId);

            return await StartBillCheckoutAsync(bill.Id, customerId, urls);
        }

        public async Task<string> StartBillCheckoutAsync(
            int billId, string customerId, OnlineCheckoutUrls urls)
        {
            RequireAvailable();
            ValidateCustomer(customerId);
            ValidateUrls(urls);

            if (billId <= 0)
                throw new InvalidOperationException("Bill not found.");

            return await _billingTransaction.ExecuteForBillPaymentAsync(
                billId,
                async () =>
                {
                    var bill = await _bills.Query()
                        .AsNoTracking()
                        .Include(b => b.Orders)
                        .Include(b => b.DiningSession)
                        .FirstOrDefaultAsync(b => b.Id == billId);

                    if (bill == null || !CustomerOwnsBill(bill, customerId))
                        throw new InvalidOperationException("Bill not found.");

                    if (bill.Status == BillStatus.Paid)
                        throw new InvalidOperationException("This bill has already been paid.");

                    if (bill.Status != BillStatus.Confirmed)
                        throw new InvalidOperationException(
                            "This bill is not ready for payment yet. Please wait for staff to confirm it.");

                    // Same payment-time check as the counter payment (US32):
                    // the bill must still equal its non-cancelled orders.
                    var chargeable = bill.Orders
                        .Where(o => o.Status != OrderStatus.Cancelled)
                        .ToList();

                    if (chargeable.Count == 0 ||
                        chargeable.Sum(o => o.TotalAmount) != bill.TotalAmount ||
                        bill.TotalAmount <= 0)
                    {
                        throw new InvalidOperationException(
                            "The bill amount no longer matches its orders. Please ask staff for help.");
                    }

                    if (bill.TotalAmount < StripeMinimumMyr)
                        throw new InvalidOperationException(
                            $"Online payment needs at least RM {StripeMinimumMyr:0.00}. Please pay at the counter.");

                    // An unfinished online attempt already exists?
                    // Settle it with Stripe first; reuse it if it is still open.
                    var unfinished = await _payments.Query()
                        .AsNoTracking()
                        .Where(p => p.BillId == billId && p.Status == PaymentStatus.Pending)
                        .OrderBy(p => p.Id)
                        .ToListAsync();

                    foreach (var pending in unfinished)
                    {
                        if (pending.Method != PaymentMethod.Online ||
                            string.IsNullOrEmpty(pending.TransactionRef))
                        {
                            throw new InvalidOperationException(
                                "Another payment for this bill is in progress. Please ask staff for help.");
                        }

                        var session = await ReadSessionAsync(pending);
                        var state = await ApplySessionAsync(pending.Id, session);

                        if (state == OnlinePaymentState.Paid)
                            throw new InvalidOperationException("This bill has already been paid.");

                        if (state == OnlinePaymentState.StillPending &&
                            session.IsOpen &&
                            !string.IsNullOrEmpty(session.Url))
                        {
                            // Customer pressed "Pay online" again: same Stripe page.
                            return session.Url;
                        }
                    }

                    DateTime now = DateTime.Now;   // team rule: local server time

                    var payment = new Payment
                    {
                        BillId = billId,
                        OrderId = null,
                        Amount = bill.TotalAmount,
                        Method = PaymentMethod.Online,
                        Status = PaymentStatus.Pending,
                        TransactionRef = null,
                        CreatedAt = now,
                        PaidAt = null,
                        RecordedByUserId = null
                    };

                    await _payments.AddAsync(payment);
                    await _payments.SaveChangesAsync();

                    // Stripe needs at least 30 minutes; our own limit is
                    // CheckoutMinutes and is enforced when we check the session.
                    int stripeMinutes = Math.Max(31, _settings.CheckoutMinutes + 1);

                    var created = await _stripe.CreateSessionAsync(new StripeCheckoutRequest(
                        payment.Id,
                        billId,
                        ToSen(bill.TotalAmount),
                        _settings.Currency,
                        $"{Brand.FullName} · Bill {BillNo(billId)}",
                        urls.SuccessUrl,
                        urls.CancelUrl.Replace(
                            OnlineCheckoutUrls.PaymentIdToken,
                            payment.Id.ToString(),
                            StringComparison.Ordinal),
                        DateTime.UtcNow.AddMinutes(stripeMinutes)));

                    if (string.IsNullOrEmpty(created.Url))
                        throw new InvalidOperationException(
                            "Stripe did not return a payment page. Please try again.");

                    int saved = await _payments.Query()
                        .Where(p => p.Id == payment.Id && p.Status == PaymentStatus.Pending)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.TransactionRef, created.Id));

                    if (saved != 1)
                        throw new InvalidOperationException(
                            "The payment changed while it was being started. Please try again.");

                    return created.Url;
                });
        }

        // ---------------------------------------------------------------
        // Finish
        // ---------------------------------------------------------------

        public async Task<OnlinePaymentOutcome> CompleteCheckoutAsync(
            string sessionId, string customerId)
        {
            var payment = await FindOwnPaymentAsync(sessionId, null, customerId);
            if (payment == null)
                return new(OnlinePaymentState.NotFound, null, null, null, null);

            await SettleAsync(payment, expireIfOpen: false);
            return await OutcomeAsync(payment.Id);
        }

        public async Task<OnlinePaymentOutcome> CancelCheckoutAsync(
            int paymentId, string customerId)
        {
            var payment = await FindOwnPaymentAsync(null, paymentId, customerId);
            if (payment == null)
                return new(OnlinePaymentState.NotFound, null, null, null, null);

            await SettleAsync(payment, expireIfOpen: true);
            return await OutcomeAsync(payment.Id);
        }

        public async Task ReconcileBillAsync(int billId)
        {
            if (!_stripe.IsEnabled || billId <= 0)
                return;

            var pending = await _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    p.BillId == billId &&
                    p.Status == PaymentStatus.Pending &&
                    p.Method == PaymentMethod.Online)
                .ToListAsync();

            foreach (var payment in pending)
            {
                try
                {
                    await SettleAsync(payment, expireIfOpen: false);
                }
                catch (Exception ex)
                {
                    // Stripe unreachable etc.: leave it Pending and try next time.
                    _logger.LogWarning(ex,
                        "US33: could not check online payment {PaymentId} with Stripe.", payment.Id);
                }
            }
        }

        // Ask Stripe about one payment and record the result.
        // returnToDraft: a failed takeaway attempt sends the customer-confirmed
        // bill back to Draft (not when staff cancel it to take the payment).
        private async Task<OnlinePaymentState> SettleAsync(
            Payment payment, bool expireIfOpen, bool returnToDraft = true)
        {
            if (payment.BillId is not int billId)
                return OnlinePaymentState.NotFound;

            var session = await ReadSessionAsync(payment, expireIfOpen);

            var state = await _billingTransaction.ExecuteForBillPaymentAsync(
                billId,
                () => ApplySessionAsync(payment.Id, session));

            if (state == OnlinePaymentState.Failed && returnToDraft)
                await ReturnTakeawayBillToDraftIfUnpaidAsync(billId);

            return state;
        }

        // ---------------------------------------------------------------
        // Staff side
        // ---------------------------------------------------------------

        public async Task<OnlineBillInfo> GetOnlineBillInfoAsync(int billId)
        {
            var info = await _bills.Query()
                .AsNoTracking()
                .Where(b => b.Id == billId)
                .Select(b => new
                {
                    b.DiningSessionId,
                    b.ConfirmedByUserId,
                    OrderUserId = b.Orders.Select(o => o.UserId).FirstOrDefault(),
                    Pending = b.Payments
                        .Where(p => p.Status == PaymentStatus.Pending && p.Method == PaymentMethod.Online)
                        .OrderByDescending(p => p.Id)
                        .Select(p => new { p.Id, p.CreatedAt })
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (info == null)
                return new OnlineBillInfo(false, null, null, null);

            bool byCustomer =
                info.DiningSessionId == null &&
                info.ConfirmedByUserId != null &&
                info.ConfirmedByUserId == info.OrderUserId;

            return new OnlineBillInfo(
                byCustomer,
                info.Pending?.Id,
                info.Pending?.CreatedAt,
                info.Pending?.CreatedAt.Add(CheckoutTime));
        }

        public async Task<OnlinePaymentState> CancelForCounterPaymentAsync(int billId)
        {
            if (billId <= 0)
                throw new InvalidOperationException("Bill not found.");

            var pending = await _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    p.BillId == billId &&
                    p.Status == PaymentStatus.Pending &&
                    p.Method == PaymentMethod.Online)
                .ToListAsync();

            if (pending.Count == 0)
                return OnlinePaymentState.NotFound;

            if (!_stripe.IsEnabled)
                throw new InvalidOperationException(
                    "Stripe is not set up on this computer, so the online payment cannot be checked or cancelled here.");

            var result = OnlinePaymentState.Failed;

            foreach (var payment in pending)
            {
                var state = await SettleAsync(payment, expireIfOpen: true, returnToDraft: false);

                if (state == OnlinePaymentState.Paid)
                    result = OnlinePaymentState.Paid;
                else if (state == OnlinePaymentState.StillPending && result != OnlinePaymentState.Paid)
                    result = OnlinePaymentState.StillPending;
            }

            return result;
        }

        public async Task ReconcileAllPendingAsync()
        {
            if (!_stripe.IsEnabled)
                return;

            var billIds = await _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    p.Status == PaymentStatus.Pending &&
                    p.Method == PaymentMethod.Online &&
                    p.BillId != null)
                .Select(p => p.BillId!.Value)
                .Distinct()
                .Take(20)
                .ToListAsync();

            foreach (int billId in billIds)
                await ReconcileBillAsync(billId);
        }

        // Read the session; close it at Stripe when it is too old (or the
        // customer cancelled), so it can never be paid after we mark it Failed.
        private async Task<StripeCheckoutSession> ReadSessionAsync(
            Payment payment, bool expireNow = false)
        {
            string sessionId = payment.TransactionRef
                ?? throw new InvalidOperationException("Online payment has no Stripe session.");

            var session = await _stripe.GetSessionAsync(sessionId);

            bool timedOut = DateTime.Now - payment.CreatedAt > CheckoutTime;

            if (session.IsOpen && (expireNow || timedOut))
                session = await _stripe.ExpireSessionAsync(sessionId);

            return session;
        }

        // Runs INSIDE the bill payment lock. Records what Stripe says.
        private async Task<OnlinePaymentState> ApplySessionAsync(
            int paymentId, StripeCheckoutSession session)
        {
            var payment = await _payments.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null)
                return OnlinePaymentState.NotFound;

            if (payment.Status == PaymentStatus.Paid)
                return OnlinePaymentState.Paid;   // already recorded: do nothing

            if (session.IsPaid)
            {
                bool matches =
                    session.AmountTotal == ToSen(payment.Amount) &&
                    string.Equals(session.Currency, _settings.Currency, StringComparison.OrdinalIgnoreCase) &&
                    (!session.Metadata.TryGetValue("rroms_payment_id", out var metaId) ||
                     metaId == payment.Id.ToString());

                if (!matches)
                {
                    _logger.LogError(
                        "US33: Stripe session {SessionId} was paid but does not match payment {PaymentId}. Left for staff to check.",
                        session.Id, payment.Id);
                    return OnlinePaymentState.StillPending;
                }

                DateTime now = DateTime.Now;

                // Pending -> Paid. Failed -> Paid is allowed too: Stripe took
                // the money, so the record must say so.
                int paid = await _payments.Query()
                    .Where(p =>
                        p.Id == paymentId &&
                        (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Failed))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, PaymentStatus.Paid)
                        .SetProperty(p => p.PaidAt, (DateTime?)now));

                if (paid != 1)
                    return OnlinePaymentState.Paid;   // another request recorded it

                if (payment.BillId is int billId)
                {
                    int billPaid = await _bills.Query()
                        .Where(b => b.Id == billId && b.Status == BillStatus.Confirmed)
                        .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BillStatus.Paid));

                    if (billPaid != 1)
                    {
                        // Late payment after the takeaway bill went back to Draft.
                        billPaid = await _bills.Query()
                            .Where(b => b.Id == billId && b.Status == BillStatus.Draft)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(b => b.Status, BillStatus.Paid)
                                .SetProperty(b => b.ConfirmedAt, (DateTime?)now));
                    }

                    if (billPaid != 1)
                    {
                        // Bill was already paid (e.g. at the counter). The money
                        // is real, so the payment stays Paid; US40 flags bills
                        // paid twice for staff to refund by hand.
                        _logger.LogWarning(
                            "US33: bill {BillId} was already paid when online payment {PaymentId} arrived.",
                            billId, paymentId);
                    }
                }

                return OnlinePaymentState.Paid;
            }

            if (session.IsExpired)
            {
                await _payments.Query()
                    .Where(p => p.Id == paymentId && p.Status == PaymentStatus.Pending)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, PaymentStatus.Failed)
                        .SetProperty(p => p.PaidAt, (DateTime?)null));

                return OnlinePaymentState.Failed;
            }

            return payment.Status == PaymentStatus.Failed
                ? OnlinePaymentState.Failed
                : OnlinePaymentState.StillPending;
        }

        // ---------------------------------------------------------------
        // Status for Member 3's button, and the customer pay page list
        // ---------------------------------------------------------------

        public async Task<TakeawayPayStatus> GetTakeawayPayStatusAsync(int orderId)
        {
            if (orderId <= 0)
                return TakeawayPayStatus.PayAtCounter;

            var order = await _orders.Query()
                .AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new
                {
                    o.OrderType,
                    o.Status,
                    o.TotalAmount,
                    o.BillId,
                    BillStatus = o.Bill != null ? (BillStatus?)o.Bill.Status : null,
                    LegacyPaid = o.Payments.Any(p => p.Status == PaymentStatus.Paid)
                })
                .FirstOrDefaultAsync();

            if (order == null || order.OrderType != OrderType.Takeaway)
                return TakeawayPayStatus.PayAtCounter;

            // "Paid" is shown even if Stripe is not set up on this laptop.
            if (order.BillStatus == BillStatus.Paid || order.LegacyPaid)
                return TakeawayPayStatus.Paid;

            if (order.Status == OrderStatus.Cancelled ||
                !_stripe.IsEnabled ||
                order.TotalAmount < StripeMinimumMyr)
            {
                return TakeawayPayStatus.PayAtCounter;
            }

            if (order.BillId is int billId)
            {
                // Settle an unfinished attempt first (paid / expired / timed out).
                await ReconcileBillAsync(billId);

                var now = await _bills.Query()
                    .AsNoTracking()
                    .Where(b => b.Id == billId)
                    .Select(b => new
                    {
                        b.Status,
                        Pending = b.Payments.Any(p => p.Status == PaymentStatus.Pending)
                    })
                    .FirstOrDefaultAsync();

                if (now?.Status == BillStatus.Paid)
                    return TakeawayPayStatus.Paid;

                if (now?.Pending == true)
                    return TakeawayPayStatus.InProgress;
            }

            return TakeawayPayStatus.CanPay;
        }

        public async Task<IReadOnlyList<PayableItem>> GetPayableItemsAsync(string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId))
                return Array.Empty<PayableItem>();

            // 1. Settle unfinished online attempts of this customer's bills,
            //    so the list shows the real state.
            var pendingBillIds = await _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    p.Status == PaymentStatus.Pending &&
                    p.Method == PaymentMethod.Online &&
                    p.Bill != null &&
                    p.Bill.Orders.Any(o => o.UserId == customerId))
                .Select(p => p.BillId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (int billId in pendingBillIds)
                await ReconcileBillAsync(billId);

            // 2. Dine-in: combined bills confirmed by staff and not paid yet.
            var dineIn = await _bills.Query()
                .AsNoTracking()
                .Where(b =>
                    b.DiningSessionId != null &&
                    b.Status == BillStatus.Confirmed &&
                    (b.DiningSession!.CustomerId == customerId ||
                     (b.DiningSession.CustomerId == null &&
                      b.Orders.Any(o => o.UserId == customerId))))
                .Select(b => new
                {
                    b.Id,
                    Table = b.DiningSession!.Table.TableNumber,
                    b.TotalAmount,
                    OrderCount = b.Orders.Count(o => o.Status != OrderStatus.Cancelled),
                    b.ConfirmedAt,
                    Opened = b.DiningSession.OpenedAt,
                    PendingSince = b.Payments
                        .Where(p => p.Status == PaymentStatus.Pending)
                        .Select(p => (DateTime?)p.CreatedAt)
                        .Max()
                })
                .ToListAsync();

            // 3. Takeaway: this customer's orders not cancelled and not paid.
            var takeaway = await _orders.Query()
                .AsNoTracking()
                .Where(o =>
                    o.UserId == customerId &&
                    o.OrderType == OrderType.Takeaway &&
                    o.Status != OrderStatus.Cancelled &&
                    (o.Bill == null || o.Bill.Status != BillStatus.Paid) &&
                    !o.Payments.Any(p => p.Status == PaymentStatus.Paid))
                .Select(o => new
                {
                    o.Id,
                    o.OrderNo,
                    o.BillId,
                    o.TotalAmount,
                    o.Status,
                    o.CreatedAt,
                    PendingSince = o.Bill == null
                        ? null
                        : o.Bill.Payments
                            .Where(p => p.Status == PaymentStatus.Pending)
                            .Select(p => (DateTime?)p.CreatedAt)
                            .Max()
                })
                .ToListAsync();

            TakeawayPayStatus StatusFor(decimal amount, DateTime? pendingSince)
            {
                if (pendingSince != null) return TakeawayPayStatus.InProgress;
                if (!_stripe.IsEnabled || amount < StripeMinimumMyr) return TakeawayPayStatus.PayAtCounter;
                return TakeawayPayStatus.CanPay;
            }

            var items = new List<PayableItem>();

            items.AddRange(dineIn.Select(b => new PayableItem(
                PayableKind.DineIn, b.Id, null, b.Table, b.TotalAmount, b.OrderCount,
                b.ConfirmedAt, b.ConfirmedAt ?? b.Opened, null,
                StatusFor(b.TotalAmount, b.PendingSince), b.PendingSince)));

            items.AddRange(takeaway.Select(o => new PayableItem(
                PayableKind.Takeaway, o.BillId, o.Id, o.OrderNo, o.TotalAmount, 1,
                null, o.CreatedAt, o.Status,
                StatusFor(o.TotalAmount, o.PendingSince), o.PendingSince)));

            // Newest first.
            return items.OrderByDescending(i => i.CreatedAt).ToList();
        }

        // ---------------------------------------------------------------
        // Takeaway bill: auto-confirm and back to Draft
        // ---------------------------------------------------------------

        // Runs in the takeaway order lock (same lock as Member 3's order
        // cancellation), so the order cannot be cancelled at the same moment.
        private Task ConfirmTakeawayBillForOnlinePaymentAsync(
            int orderId, int billId, string customerId)
        {
            return _billingTransaction.ExecuteForTakeawayOrderAsync(
                orderId,
                async () =>
                {
                    var order = await _orders.Query()
                        .AsNoTracking()
                        .Where(o => o.Id == orderId)
                        .Select(o => new
                        {
                            o.Status, o.BillId, o.Subtotal, o.TaxAmount,
                            o.ServiceCharge, o.TotalAmount
                        })
                        .FirstOrDefaultAsync();

                    if (order == null || order.BillId != billId)
                        throw new InvalidOperationException(
                            "The order changed. Please refresh and try again.");

                    if (order.Status == OrderStatus.Cancelled)
                        throw new InvalidOperationException("This order was cancelled.");

                    DateTime now = DateTime.Now;

                    // Draft -> Confirmed only while the bill still equals the order.
                    int confirmed = await _bills.Query()
                        .Where(b =>
                            b.Id == billId &&
                            b.Status == BillStatus.Draft &&
                            b.DiningSessionId == null &&
                            b.Subtotal == order.Subtotal &&
                            b.TaxAmount == order.TaxAmount &&
                            b.ServiceCharge == order.ServiceCharge &&
                            b.TotalAmount == order.TotalAmount)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.Status, BillStatus.Confirmed)
                            .SetProperty(b => b.ConfirmedAt, (DateTime?)now)
                            // Confirmed by the customer choosing to pay online.
                            .SetProperty(b => b.ConfirmedByUserId, customerId));

                    if (confirmed != 1)
                    {
                        var status = await _bills.Query()
                            .AsNoTracking()
                            .Where(b => b.Id == billId)
                            .Select(b => (BillStatus?)b.Status)
                            .FirstOrDefaultAsync();

                        // Staff (or a double click) confirmed it first: fine.
                        if (status != BillStatus.Confirmed)
                            throw new InvalidOperationException(
                                "The bill changed. Please refresh and try again.");
                    }

                    return confirmed;
                });
        }

        // Only a takeaway bill that the CUSTOMER confirmed for online payment,
        // and only when no payment is Pending or Paid. A bill confirmed by
        // staff stays Confirmed.
        private async Task ReturnTakeawayBillToDraftIfUnpaidAsync(int billId)
        {
            var info = await _bills.Query()
                .AsNoTracking()
                .Where(b => b.Id == billId && b.DiningSessionId == null)
                .Select(b => new
                {
                    b.Status,
                    b.ConfirmedByUserId,
                    Order = b.Orders.Select(o => new { o.Id, o.UserId }).FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (info?.Order == null ||
                info.Status != BillStatus.Confirmed ||
                info.ConfirmedByUserId != info.Order.UserId)
            {
                return;
            }

            int orderId = info.Order.Id;
            string customerId = info.Order.UserId;

            await _billingTransaction.ExecuteForTakeawayOrderAsync(
                orderId,
                async () =>
                {
                    bool active = await _payments.Query()
                        .AsNoTracking()
                        .AnyAsync(p =>
                            p.BillId == billId &&
                            (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Paid));

                    if (active)
                        return 0;

                    return await _bills.Query()
                        .Where(b =>
                            b.Id == billId &&
                            b.Status == BillStatus.Confirmed &&
                            b.ConfirmedByUserId == customerId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.Status, BillStatus.Draft)
                            .SetProperty(b => b.ConfirmedAt, (DateTime?)null)
                            .SetProperty(b => b.ConfirmedByUserId, (string?)null));
                });
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        // Find an online payment by Stripe session id OR by payment id,
        // only if its bill belongs to this customer.
        private async Task<Payment?> FindOwnPaymentAsync(
            string? sessionId, int? paymentId, string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId) || !_stripe.IsEnabled)
                return null;

            var query = _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    p.Method == PaymentMethod.Online &&
                    p.BillId != null &&
                    p.TransactionRef != null);

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                string id = sessionId.Trim();
                query = query.Where(p => p.TransactionRef == id);
            }
            else if (paymentId is int pid && pid > 0)
            {
                query = query.Where(p => p.Id == pid);
            }
            else
            {
                return null;
            }

            var payment = await query.FirstOrDefaultAsync();

            if (payment == null)
                return null;

            var bill = await _bills.Query()
                .AsNoTracking()
                .Include(b => b.Orders)
                .Include(b => b.DiningSession)
                .FirstOrDefaultAsync(b => b.Id == payment.BillId);

            return bill != null && CustomerOwnsBill(bill, customerId) ? payment : null;
        }

        private async Task<OnlinePaymentOutcome> OutcomeAsync(int paymentId)
        {
            var p = await _payments.Query()
                .AsNoTracking()
                .FirstAsync(x => x.Id == paymentId);

            var state = p.Status switch
            {
                PaymentStatus.Paid => OnlinePaymentState.Paid,
                PaymentStatus.Pending => OnlinePaymentState.StillPending,
                _ => OnlinePaymentState.Failed
            };

            // Takeaway bill: the order, so the result page can link back to it.
            var takeaway = await _bills.Query()
                .AsNoTracking()
                .Where(b => b.Id == p.BillId && b.DiningSessionId == null)
                .SelectMany(b => b.Orders)
                .Select(o => new { o.Id, o.OrderNo })
                .FirstOrDefaultAsync();

            return new(state, p.BillId, p.Id, p.Amount, p.PaidAt,
                takeaway?.Id, takeaway?.OrderNo);
        }

        // Dine-in: the dining session's customer (from the reservation).
        // Takeaway: the customer who placed the order.
        private static bool CustomerOwnsBill(Bill bill, string customerId)
        {
            if (bill.DiningSessionId != null)
            {
                return bill.DiningSession?.CustomerId == customerId ||
                       (bill.DiningSession?.CustomerId == null &&
                        bill.Orders.Any(o => o.UserId == customerId));
            }

            return bill.Orders.Any(o => o.UserId == customerId);
        }

        private static long ToSen(decimal amount)
            => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

        private static string BillNo(int billId) => $"YB-{billId:D6}";

        private void RequireAvailable()
        {
            if (!_stripe.IsEnabled)
                throw new InvalidOperationException(
                    "Online payment is not available right now. Please pay at the counter.");
        }

        private static void ValidateCustomer(string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId))
                throw new InvalidOperationException("Please log in to pay online.");
        }

        private static void ValidateUrls(OnlineCheckoutUrls urls)
        {
            if (urls == null ||
                string.IsNullOrWhiteSpace(urls.SuccessUrl) ||
                string.IsNullOrWhiteSpace(urls.CancelUrl) ||
                !urls.SuccessUrl.Contains(OnlineCheckoutUrls.SessionIdToken, StringComparison.Ordinal) ||
                !urls.CancelUrl.Contains(OnlineCheckoutUrls.PaymentIdToken, StringComparison.Ordinal))
            {
                throw new ArgumentException("Success and cancel URLs are required.");
            }
        }
    }
}
