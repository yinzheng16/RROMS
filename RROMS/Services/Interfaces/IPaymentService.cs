using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // US31: Contract for recording and retrieving payments.
    public interface IPaymentService
    {
        Task<Payment> RecordPaymentAsync(
            int orderId,
            decimal amount,
            PaymentMethod method,
            PaymentStatus status,
            string? transactionRef = null);

        Task<Payment?> GetPaymentByIdAsync(int paymentId);

        Task<List<Payment>> GetPaymentsByOrderIdAsync(int orderId);

        Task<Payment> UpdatePaymentStatusAsync(
            int paymentId,
            PaymentStatus newStatus);
    }
}
