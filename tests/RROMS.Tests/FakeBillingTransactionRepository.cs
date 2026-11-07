using RROMS.Repositories;

namespace RROMS.Tests;

// Test double for IBillingTransactionRepository.
// The real repository uses SQL Server sp_getapplock, which SQLite does not
// support. This fake skips the lock/transaction and just runs the operation,
// so PaymentService logic can be tested on its own.
public class FakeBillingTransactionRepository : IBillingTransactionRepository
{
    public Task<T> ExecuteForTakeawayOrderAsync<T>(
        int orderId,
        Func<Task<T>> operation) => operation();

    public Task<T> ExecuteForSessionBillAsync<T>(
        int sessionId,
        Func<Task<T>> operation) => operation();

    public Task<T> ExecuteForBillPaymentAsync<T>(
        int billId,
        Func<Task<T>> operation) => operation();
}
