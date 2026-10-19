
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // US31: Payment transaction recording and retrieval.
    public class PaymentService : IPaymentService
    {
        private readonly IRepository<Payment> _payments; // Saving and checking payment transactions
        private readonly IRepository<Order> _orders; // Confirm that the order is exist

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
            PaymentStatus status,
            string? transactionRef = null)
        {
            // Validate input values.
            if (orderId <= 0)
                throw new ArgumentException("Invalid order ID.");

            if (amount <= 0)
                throw new ArgumentException(
                    "Payment amount must be greater than zero.");

            if (decimal.Round(amount, 2) != amount)
                throw new ArgumentException(
                    "Payment amount cannot exceed two decimal places.");

            if (!Enum.IsDefined(method))
                throw new ArgumentException("Invalid payment method.");

            if (!Enum.IsDefined(status))
                throw new ArgumentException("Invalid payment status.");

            transactionRef = transactionRef?.Trim();

            if (transactionRef?.Length > 100)
                throw new ArgumentException(
                    "Transaction reference cannot exceed 100 characters.");

            // Verify that the order exists.
            var order = await _orders.GetByIdAsync(orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "Cannot record payment for a cancelled order.");

            // Prevent recording another successful payment
            // when the order has already been paid in full.
            bool alreadyPaid = await _payments.Query()
                .AnyAsync(p =>
                    p.OrderId == orderId &&
                    p.Status == PaymentStatus.Paid);

            if (alreadyPaid && status == PaymentStatus.Paid)
                throw new InvalidOperationException(
                    "This order already has a successful payment.");

            // Create a new payment record.
            var payment = new Payment
            {
                OrderId = orderId,
                Amount = amount,
                Method = method,
                Status = status,
                TransactionRef = transactionRef,
                CreatedAt = DateTime.Now,
                PaidAt = status == PaymentStatus.Paid
                    ? DateTime.Now
                    : null
            };

            await _payments.AddAsync(payment);
            await _payments.SaveChangesAsync();

            return payment;
        }

        public Task<Payment?> GetPaymentByIdAsync(int paymentId)
        {
            return _payments.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paymentId);
        }

        public Task<List<Payment>> GetPaymentsByOrderIdAsync(
            int orderId)
        {
            return _payments.Query()
                .AsNoTracking()
                .Where(p => p.OrderId == orderId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<Payment> UpdatePaymentStatusAsync(
            int paymentId,
            PaymentStatus newStatus)
        {
            if (!Enum.IsDefined(newStatus))
                throw new ArgumentException("Invalid payment status.");

            var payment = await _payments.GetByIdAsync(paymentId);

            if (payment == null)
                throw new InvalidOperationException(
                    "Payment does not exist.");

            if (payment.Status == PaymentStatus.Refunded)
                throw new InvalidOperationException(
                    "A refunded payment cannot be changed.");

            if (payment.Status == PaymentStatus.Paid &&
                newStatus != PaymentStatus.Paid)
                throw new InvalidOperationException(
                    "A paid transaction requires a separate refund workflow.");

            if (newStatus == PaymentStatus.Refunded)
                throw new InvalidOperationException(
                    "Refunds require a separate refund workflow.");

            if (newStatus == PaymentStatus.Paid)
            {
                bool alreadyPaid = await _payments.Query()
                    .AnyAsync(p =>
                        p.OrderId == payment.OrderId &&
                        p.Id != paymentId &&
                        p.Status == PaymentStatus.Paid);

                if (alreadyPaid)
                    throw new InvalidOperationException(
                        "This order already has a successful payment.");

                payment.PaidAt ??= DateTime.Now;
            }

            payment.Status = newStatus;

            await _payments.SaveChangesAsync();

            return payment;
        }
    }
}
