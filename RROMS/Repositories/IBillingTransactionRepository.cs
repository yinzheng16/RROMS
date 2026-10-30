namespace RROMS.Repositories
{
    // US32: Database transaction + SQL Server application lock for billing.
    public interface IBillingTransactionRepository
    {
        // Takeaway: one lock per Order (resource "RROMS:TakeawayBill:{orderId}").
        Task<T> ExecuteForTakeawayOrderAsync<T>(
            int orderId,
            Func<Task<T>> operation);

        // Dine-in: one lock per DiningSession (resource "RROMS:SessionBill:{sessionId}").
        // Bill confirmation for a session runs inside this lock.
        Task<T> ExecuteForSessionBillAsync<T>(
            int sessionId,
            Func<Task<T>> operation);

        // Payment: one lock per Bill (resource "RROMS:BillPayment:{billId}").
        // Recording a payment and marking the bill Paid run inside this lock.
        Task<T> ExecuteForBillPaymentAsync<T>(
            int billId,
            Func<Task<T>> operation);
    }
}
