using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    public interface IPaymentService
    {
        // Create only a new pending payment attempt.
        Task<Payment> RecordPaymentAsync(
            int orderId,
            decimal amount,
            PaymentMethod method,
            string? transactionRef = null);

        // Confirm that payment was actually received. （which can let pending -> paid）
        Task<Payment> ConfirmPaymentAsync(int paymentId);

        // Mark a pending payment attempt as failed. （pending -> failed)
        Task<Payment> MarkPaymentFailedAsync(int paymentId);

        // Retrieve payment records.
        Task<Payment?> GetPaymentByIdAsync(int paymentId);

        Task<List<Payment>> GetPaymentsByOrderIdAsync(
            int orderId);

        // US32: Staff record a counter payment (Cash or Card) for the
        // full amount of a Confirmed bill. In one transaction this
        // creates a Paid payment and moves the bill Confirmed -> Paid.
        // transactionRef: optional card terminal reference.
        Task<Payment> RecordCounterPaymentAsync(
            int billId,
            PaymentMethod method,
            string staffUserId,
            string? transactionRef = null);

        // US32: Payments recorded against a bill, newest first.
        Task<List<Payment>> GetPaymentsByBillIdAsync(int billId);
    }
}