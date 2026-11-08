using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly IRepository<Payment> _payments;
        private readonly IRepository<Order> _orders;
        private readonly IRepository<Bill> _bills;
        private readonly IBillingTransactionRepository _billingTransaction;

        // US38: optional, so existing tests that build PaymentService by hand still compile.
        private readonly INotificationService? _notifications;
        private readonly UserManager<ApplicationUser>? _users;
        private readonly ILogger<PaymentService>? _logger;

        public PaymentService(
            IRepository<Payment> payments,
            IRepository<Order> orders,
            IRepository<Bill> bills,
            IBillingTransactionRepository billingTransaction,
            INotificationService? notifications = null,
            UserManager<ApplicationUser>? users = null,
            ILogger<PaymentService>? logger = null)
        {
            _payments = payments;
            _orders = orders;
            _bills = bills;
            _billingTransaction = billingTransaction;
            _notifications = notifications;
            _users = users;
            _logger = logger;
        }

        // US32: Counter payment against a Confirmed bill (one step).
        // At the counter the money is received immediately, so the
        // payment is recorded as Paid and the bill becomes Paid in the
        // same transaction. Pending/Failed are for online payments (US33).
        // Full payment only (split / partial payments are future work).
        public async Task<Payment> RecordCounterPaymentAsync(
            int billId,
            PaymentMethod method,
            string staffUserId,
            string? transactionRef = null)
        {
            if (billId <= 0)
                throw new ArgumentException("Invalid bill ID.");

            if (method != PaymentMethod.Cash &&
                method != PaymentMethod.Card)
            {
                throw new ArgumentException(
                    "Counter payments must be Cash or Card. " +
                    "Online payments are handled by online checkout.");
            }

            if (string.IsNullOrWhiteSpace(staffUserId))
                throw new ArgumentException("Staff user ID is required.");

            string recordedBy = staffUserId.Trim();

            transactionRef = transactionRef?.Trim();

            if (string.IsNullOrWhiteSpace(transactionRef))
                transactionRef = null;

            if (transactionRef?.Length > 100)
                throw new ArgumentException(
                    "Transaction reference cannot exceed 100 characters.");

            var paid = await _billingTransaction.ExecuteForBillPaymentAsync(
                billId,
                async () =>
                {
                    var bill = await _bills.Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Id == billId);

                    if (bill == null)
                        throw new InvalidOperationException(
                            "Bill does not exist.");

                    if (bill.Status == BillStatus.Paid)
                        throw new InvalidOperationException(
                            "This bill has already been paid.");

                    if (bill.Status != BillStatus.Confirmed)
                        throw new InvalidOperationException(
                            "Only confirmed bills can be paid. Please confirm the bill first.");

                    if (bill.TotalAmount <= 0)
                        throw new InvalidOperationException(
                            "Bill total is not valid.");

                    // Payment-time check: the bill must still equal the
                    // saved totals of its (non-cancelled) orders.
                    var orderTotals = await _orders.Query()
                        .AsNoTracking()
                        .Where(o =>
                            o.BillId == billId &&
                            o.Status != OrderStatus.Cancelled)
                        .Select(o => o.TotalAmount)
                        .ToListAsync();

                    if (orderTotals.Count == 0 ||
                        orderTotals.Sum() != bill.TotalAmount)
                    {
                        throw new InvalidOperationException(
                            "The bill amount no longer matches its orders. " +
                            "Payment cannot be recorded for this bill.");
                    }

                    bool onlinePaymentInProgress = await _payments.Query()
                        .AsNoTracking()
                        .AnyAsync(p =>
                            p.BillId == billId &&
                            p.Status == PaymentStatus.Pending);

                    if (onlinePaymentInProgress)
                        throw new InvalidOperationException(
                            "An online payment for this bill is still in progress.");

                    if (transactionRef != null)
                    {
                        bool referenceExists = await _payments.Query()
                            .AsNoTracking()
                            .AnyAsync(p => p.TransactionRef == transactionRef);

                        if (referenceExists)
                            throw new InvalidOperationException(
                                "Transaction reference already exists.");
                    }

                    DateTime now = DateTime.Now;   // team rule: local server time

                    var payment = new Payment
                    {
                        BillId = billId,
                        OrderId = null,
                        Amount = bill.TotalAmount,
                        Method = method,
                        Status = PaymentStatus.Paid,
                        TransactionRef = transactionRef,
                        CreatedAt = now,
                        PaidAt = now,
                        RecordedByUserId = recordedBy
                    };

                    await _payments.AddAsync(payment);
                    await _payments.SaveChangesAsync();

                    // Conditional update: only one request can move the
                    // bill from Confirmed to Paid. If it fails, throwing
                    // rolls back the payment inserted above.
                    int updated = await _bills.Query()
                        .Where(b =>
                            b.Id == billId &&
                            b.Status == BillStatus.Confirmed)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(b => b.Status, BillStatus.Paid));

                    if (updated != 1)
                        throw new InvalidOperationException(
                            "This bill has already been paid.");

                    return payment;
                });

            // US38: notify the customer only after the payment is saved
            // (outside the transaction, so an email can never undo it).
            await NotifyPaymentReceivedAsync(paid);

            return paid;
        }

        // ================= US38: payment received notification =================

        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        private static string Money(decimal amount) => $"RM {amount.ToString("N2", En)}";

        // In-app notification + email (NotificationService, US38) to the bill's customer.
        // Never throws: the payment is already saved.
        private async Task NotifyPaymentReceivedAsync(Payment payment)
        {
            if (_notifications is null || payment.BillId is not int billId)
                return;

            try
            {
                var bill = await _bills.Query()
                    .AsNoTracking()
                    .Include(b => b.DiningSession!)
                        .ThenInclude(s => s.Reservation)
                    .Include(b => b.Orders)
                    .FirstOrDefaultAsync(b => b.Id == billId);

                if (bill == null)
                    return;

                string? customerId = await FindCustomerAsync(bill);
                if (customerId == null)
                    return;

                bool takeaway = bill.DiningSessionId == null;
                string billNo = $"YB-{billId:D6}";   // same format as Staff/Billing (IndexModel.FormatBillNo)
                string amount = Money(payment.Amount);

                var orderNos = bill.Orders
                    .Where(o => o.Status != OrderStatus.Cancelled)
                    .OrderBy(o => o.Id)
                    .Select(o => o.OrderNo)
                    .ToList();

                var rows = new List<EmailTemplates.Row>
                {
                    new("Bill", billNo)
                };

                if (orderNos.Count > 0)
                    rows.Add(new(orderNos.Count == 1 ? "Order" : "Orders", string.Join(", ", orderNos)));

                rows.Add(new("Paid on", (payment.PaidAt ?? DateTime.Now).ToString("ddd, d MMM yyyy · h:mm tt", En)));
                rows.Add(new("Payment method", payment.Method.ToString()));
                rows.Add(new("Subtotal", Money(bill.Subtotal)));
                // Same % labels as the bill and receipt (rates saved on the charged orders).
                var (taxRate, serviceRate) = ChargeRates.SharedSaved(
                    bill.Orders.Where(o => o.Status != OrderStatus.Cancelled));
                rows.Add(new(ChargeRates.Label("Service charge", serviceRate), Money(bill.ServiceCharge)));
                rows.Add(new(ChargeRates.Label("Tax", taxRate), Money(bill.TaxAmount)));
                rows.Add(new("Total paid", amount));

                if (!string.IsNullOrEmpty(payment.TransactionRef))
                    rows.Add(new("Reference", payment.TransactionRef));

                await _notifications.SendAsync(
                    customerId,
                    "Payment received",
                    $"We received your {payment.Method} payment of {amount} for bill {billNo}. Thank you!",
                    NotificationType.Payment,
                    new NotificationEmail(
                        Subject: $"Payment received · {billNo}",
                        Kicker: "Payment received",
                        Title: takeaway ? "Thank you for your order" : "Thank you for dining with us",
                        IntroText: $"We received your payment of {amount}. Here is a summary of your bill.",
                        Rows: rows,
                        SmallText: "Keep this email as a record of your payment."));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Payment {PaymentId}: the payment-received notification was not sent.", payment.Id);
            }
        }

        // The customer of a bill: the dining session's customer, then the reservation's
        // customer, then whoever placed the orders. Only Customer accounts count, so a
        // staff member who ordered for a guest never gets the email.
        private async Task<string?> FindCustomerAsync(Bill bill)
        {
            var candidates = new List<string?>
            {
                bill.DiningSession?.CustomerId,
                bill.DiningSession?.Reservation?.UserId
            };
            candidates.AddRange(bill.Orders.OrderBy(o => o.Id).Select(o => o.UserId));

            foreach (var id in candidates.Where(x => !string.IsNullOrEmpty(x)).Distinct())
            {
                if (_users == null)
                    return id;

                var user = await _users.FindByIdAsync(id!);
                if (user != null && await _users.IsInRoleAsync(user, Roles.Customer))
                    return id;
            }

            return null;
        }

        public Task<List<Payment>> GetPaymentsByBillIdAsync(int billId)
        {
            return _payments.Query()
                .AsNoTracking()
                .Where(p => p.BillId == billId)
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .ToListAsync();
        }

        // US34: The logged-in customer's own payments, newest first.
        // Which payments belong to the customer:
        //  - dine-in combined bill: the customer of the dining session
        //  - takeaway bill: the customer who placed the order
        //  - older order-based payment (US31): the customer of that order
        // Dates are local server time (team rule), filtered on PaidAt,
        // or CreatedAt for attempts that were never paid.
        public async Task<CustomerPaymentHistory> GetCustomerPaymentsAsync(
            string customerId,
            DateOnly? from = null,
            DateOnly? to = null)
        {
            if (string.IsNullOrWhiteSpace(customerId))
                throw new ArgumentException("Customer ID is required.");

            if (from is DateOnly f && to is DateOnly t && f > t)
                throw new ArgumentException(
                    "The From date cannot be after the To date.");

            string id = customerId.Trim();

            var query = _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    (p.Bill != null &&
                     p.Bill.DiningSessionId != null &&
                     p.Bill.DiningSession!.CustomerId == id) ||
                    (p.Bill != null &&
                     p.Bill.DiningSessionId == null &&
                     p.Bill.Orders.Any(o => o.UserId == id)) ||
                    (p.BillId == null &&
                     p.Order != null &&
                     p.Order.UserId == id));

            if (from is DateOnly fromDate)
            {
                DateTime start = fromDate.ToDateTime(TimeOnly.MinValue);
                query = query.Where(p => (p.PaidAt ?? p.CreatedAt) >= start);
            }

            if (to is DateOnly toDate)
            {
                // Up to the end of the To day: before midnight of the next day.
                DateTime end = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
                query = query.Where(p => (p.PaidAt ?? p.CreatedAt) < end);
            }

            var payments = await query
                .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Select(p => new CustomerPaymentRecord
                {
                    PaymentId = p.Id,
                    BillId = p.BillId,

                    OrderType = p.Bill != null
                        ? (p.Bill.DiningSessionId != null
                            ? OrderType.DineIn
                            : OrderType.Takeaway)
                        : p.Order!.OrderType,

                    TableNumber = p.Bill != null
                        ? (p.Bill.DiningSession != null
                            ? p.Bill.DiningSession.Table.TableNumber
                            : null)
                        : (p.Order != null && p.Order.DiningSession != null
                            ? p.Order.DiningSession.Table.TableNumber
                            : null),

                    OrderNo = p.Bill != null
                        ? p.Bill.Orders
                            .OrderBy(o => o.Id)
                            .Select(o => o.OrderNo)
                            .FirstOrDefault()
                        : p.Order!.OrderNo,

                    OrderCount = p.Bill != null
                        ? p.Bill.Orders.Count(o => o.Status != OrderStatus.Cancelled)
                        : 1,

                    Amount = p.Amount,
                    Method = p.Method,
                    Status = p.Status,
                    TransactionRef = p.TransactionRef,
                    CreatedAt = p.CreatedAt,
                    PaidAt = p.PaidAt
                })
                .ToListAsync();

            var paid = payments
                .Where(p => p.Status == PaymentStatus.Paid)
                .ToList();

            return new CustomerPaymentHistory
            {
                Payments = payments,
                TotalPaid = paid.Sum(p => p.Amount),
                PaidCount = paid.Count
            };
        }

        // US26 integration: a legacy payment attempt and order cancellation must not both succeed.
        // Counter payments already require a Confirmed bill, which locks cancellation.
        private async Task<Payment> WithLegacyOrderLockAsync(int orderId, Func<Task<Payment>> operation)
        {
            if (orderId <= 0) throw new ArgumentException("Invalid order ID.");
            var before = await _orders.Query().AsNoTracking().Where(o => o.Id == orderId)
                .Select(o => new { o.DiningSessionId }).SingleOrDefaultAsync();
            if (before == null) throw new InvalidOperationException("Order does not exist.");
            async Task<Payment> CheckAndRun()
            {
                var current = await _orders.Query().AsNoTracking().Where(o => o.Id == orderId)
                    .Select(o => new { o.DiningSessionId }).SingleOrDefaultAsync();
                if (current == null || current.DiningSessionId != before.DiningSessionId)
                    throw new InvalidOperationException("The order's dining session changed. Refresh before recording payment.");
                return await operation();
            }
            return before.DiningSessionId is int sessionId
                ? await _billingTransaction.ExecuteForSessionBillAsync(sessionId, CheckAndRun)
                : await _billingTransaction.ExecuteForTakeawayOrderAsync(orderId, CheckAndRun);
        }

        public Task<Payment> RecordPaymentAsync(int orderId, decimal amount, PaymentMethod method, string? transactionRef = null)
            => WithLegacyOrderLockAsync(orderId, () => RecordOrderPaymentCoreAsync(orderId, amount, method, transactionRef));

        public async Task<Payment> ConfirmPaymentAsync(int paymentId)
        {
            var payment = await _payments.Query().AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId);
            if (payment == null) throw new InvalidOperationException("Payment does not exist.");
            if (payment.OrderId is not int orderId)
                throw new InvalidOperationException("This payment belongs to a bill. Use the bill payment workflow.");
            return await WithLegacyOrderLockAsync(orderId, () => ConfirmOrderPaymentCoreAsync(paymentId));
        }

        private async Task<Payment> RecordOrderPaymentCoreAsync(
            int orderId,
            decimal amount,
            PaymentMethod method,
            string? transactionRef = null)
        {
            if (orderId <= 0)
                throw new ArgumentException("Invalid order ID.");

            if (amount <= 0 || decimal.Round(amount, 2) != amount)
                throw new ArgumentException(
                    "Payment amount must be positive and have at most two decimal places.");

            if (!Enum.IsDefined(method))
                throw new ArgumentException("Invalid payment method.");

            var order = await _orders.Query().AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "Cannot pay for a cancelled order.");

            // The bill must have a positive final total.
            if (order.TotalAmount <= 0)
                throw new InvalidOperationException(
                    "Order total has not been finalized.");

            // Full payment only; no partial payments.
            if (amount != order.TotalAmount)
                throw new InvalidOperationException(
                    "Payment amount must match the order total.");

            // Do not create another attempt while a payment
            // is already pending or successfully paid.
            bool existingActivePayment = await _payments.Query()
                .AnyAsync(p =>
                    p.OrderId == orderId &&
                    (p.Status == PaymentStatus.Pending ||
                     p.Status == PaymentStatus.Paid));

            if (existingActivePayment)
                throw new InvalidOperationException(
                    "This order already has a pending or paid transaction.");

            transactionRef = transactionRef?.Trim();

            if (string.IsNullOrWhiteSpace(transactionRef))
                transactionRef = null;

            if (transactionRef?.Length > 100)
                throw new ArgumentException(
                    "Transaction reference cannot exceed 100 characters.");

            // Prevent reuse of a known external reference.
            if (transactionRef != null)
            {
                bool referenceExists = await _payments.Query()
                    .AnyAsync(p =>
                        p.TransactionRef == transactionRef);

                if (referenceExists)
                    throw new InvalidOperationException(
                        "Transaction reference already exists.");
            }

            var payment = new Payment
            {
                OrderId = orderId,
                Amount = amount,
                Method = method,
                Status = PaymentStatus.Pending,
                TransactionRef = transactionRef,
                CreatedAt = DateTime.Now,   // team rule: local server time
                PaidAt = null
            };

            await _payments.AddAsync(payment);
            await _payments.SaveChangesAsync();

            return payment;
        }

        private async Task<Payment> ConfirmOrderPaymentCoreAsync(int paymentId)
        {
            var payment = await _payments.Query().AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null)
                throw new InvalidOperationException(
                    "Payment does not exist.");

            if (payment.Status != PaymentStatus.Pending)
                throw new InvalidOperationException(
                    "Only pending payments can be confirmed.");

            // US32: Bill payments (OrderId = null) are confirmed through
            // the bill payment workflow, not this order-based method.
            if (payment.OrderId is not int orderId)
                throw new InvalidOperationException(
                    "This payment belongs to a bill. Use the bill payment workflow.");

            var order = await _orders.Query().AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);

            if (order == null || order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "The order is missing or cancelled.");

            if (payment.Amount != order.TotalAmount)
                throw new InvalidOperationException(
                    "Payment amount no longer matches the order total.");

            bool alreadyPaid = await _payments.Query()
                .AnyAsync(p =>
                    p.OrderId == payment.OrderId &&
                    p.Id != payment.Id &&
                    p.Status == PaymentStatus.Paid);

            if (alreadyPaid)
                throw new InvalidOperationException(
                    "This order already has a successful payment.");

            DateTime now = DateTime.Now;
            int changed = await _payments.Query().Where(p => p.Id == paymentId && p.Status == PaymentStatus.Pending)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, PaymentStatus.Paid)
                    .SetProperty(p => p.PaidAt, (DateTime?)now));
            if (changed != 1) throw new InvalidOperationException("This payment is no longer pending.");
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = now;

            return payment;
        }

        public async Task<Payment> MarkPaymentFailedAsync(int paymentId)
        {
            var snapshot = await _payments.Query().AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId);
            if (snapshot == null) throw new InvalidOperationException("Payment does not exist.");
            async Task<Payment> FailPending()
            {
                // Never overwrite a payment another request has already confirmed.
                int changed = await _payments.Query().Where(p => p.Id == paymentId && p.Status == PaymentStatus.Pending)
                    .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, PaymentStatus.Failed)
                        .SetProperty(p => p.PaidAt, (DateTime?)null));
                if (changed != 1) throw new InvalidOperationException("Only pending payments can be marked as failed.");
                snapshot.Status = PaymentStatus.Failed;
                snapshot.PaidAt = null;
                return snapshot;
            }
            if (snapshot.OrderId is int orderId)
                return await WithLegacyOrderLockAsync(orderId, FailPending);
            if (snapshot.BillId is int billId)
                return await _billingTransaction.ExecuteForBillPaymentAsync(billId, FailPending);
            throw new InvalidOperationException("This payment is not linked to an order or bill.");
        }

        public Task<Payment?> GetPaymentByIdAsync(int paymentId)
        {
            return _payments.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paymentId);
        }

        public Task<List<Payment>> GetPaymentsByOrderIdAsync(int orderId)
        {
            return _payments.Query()
                .AsNoTracking()
                .Where(p => p.OrderId == orderId)
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .ToListAsync();
        }
    }
}