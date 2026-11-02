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

        public PaymentService(
            IRepository<Payment> payments,
            IRepository<Order> orders,
            IRepository<Bill> bills,
            IBillingTransactionRepository billingTransaction)
        {
            _payments = payments;
            _orders = orders;
            _bills = bills;
            _billingTransaction = billingTransaction;
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

            return await _billingTransaction.ExecuteForBillPaymentAsync(
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