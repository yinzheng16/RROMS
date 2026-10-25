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
    }
}