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

        public PaymentService(
            IRepository<Payment> payments,
            IRepository<Order> orders)
        {
            _payments = payments;
            _orders = orders;
        }

        public async Task<Payment> RecordPaymentAsync(
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

            var order = await _orders.GetByIdAsync(orderId);

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

        public async Task<Payment> ConfirmPaymentAsync(int paymentId)
        {
            var payment = await _payments.GetByIdAsync(paymentId);

            if (payment == null)
                throw new InvalidOperationException(
                    "Payment does not exist.");

            if (payment.Status != PaymentStatus.Pending)
                throw new InvalidOperationException(
                    "Only pending payments can be confirmed.");

            var order = await _orders.GetByIdAsync(payment.OrderId);

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

            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = DateTime.Now;   // team rule: local server time

            await _payments.SaveChangesAsync();

            return payment;
        }

        public async Task<Payment> MarkPaymentFailedAsync(int paymentId)
        {
            var payment = await _payments.GetByIdAsync(paymentId);

            if (payment == null)
                throw new InvalidOperationException(
                    "Payment does not exist.");

            if (payment.Status != PaymentStatus.Pending)
                throw new InvalidOperationException(
                    "Only pending payments can be marked as failed.");

            payment.Status = PaymentStatus.Failed;
            payment.PaidAt = null;

            await _payments.SaveChangesAsync();

            return payment;
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