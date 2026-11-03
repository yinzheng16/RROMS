using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;

namespace RROMS.Repositories
{
    // US32: Provides transaction-level locking for bill creation and confirmation.
    // sp_getapplock is SQL Server only; the lock is released automatically
    // when the transaction commits or rolls back.
    public class BillingTransactionRepository : IBillingTransactionRepository
    {
        private readonly ApplicationDbContext _db;

        public BillingTransactionRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public Task<T> ExecuteForTakeawayOrderAsync<T>(
            int orderId,
            Func<Task<T>> operation)
        {
            if (orderId <= 0)
                throw new ArgumentException(
                    "Invalid order ID.", nameof(orderId));

            return ExecuteWithAppLockAsync(
                $"RROMS:TakeawayBill:{orderId}",
                $"Order {orderId}",
                operation);
        }

        public Task<T> ExecuteForSessionBillAsync<T>(
            int sessionId,
            Func<Task<T>> operation)
        {
            if (sessionId <= 0)
                throw new ArgumentException(
                    "Invalid dining session ID.", nameof(sessionId));

            return ExecuteWithAppLockAsync(
                $"RROMS:SessionBill:{sessionId}",
                $"dining session {sessionId}",
                operation);
        }

        public Task<T> ExecuteForBillPaymentAsync<T>(
            int billId,
            Func<Task<T>> operation)
        {
            if (billId <= 0)
                throw new ArgumentException(
                    "Invalid bill ID.", nameof(billId));

            return ExecuteWithAppLockAsync(
                $"RROMS:BillPayment:{billId}",
                $"bill {billId}",
                operation);
        }

        private async Task<T> ExecuteWithAppLockAsync<T>(
            string resource,
            string description,
            Func<Task<T>> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);

            // The lock and the billing writes must use the same transaction.
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted);

            // sp_getapplock returns a non-negative value on success.
            // A negative value indicates failure or timeout.
            var connection = _db.Database.GetDbConnection();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = @"
                DECLARE @result int;

                EXEC @result = sp_getapplock
                    @Resource = @resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 10000;

                SELECT @result;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@resource";
            parameter.Value = resource;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
            {
                throw new InvalidOperationException(
                    $"SQL Server did not return a billing lock result for {description}.");
            }

            int lockResult = Convert.ToInt32(result);

            if (lockResult < 0)
            {
                throw new InvalidOperationException(
                    $"Could not acquire billing lock for {description}. " +
                    $"SQL Server lock result: {lockResult}.");
            }

            // Execute the billing operation while holding the lock.
            // If it throws, the transaction is disposed without commit,
            // so every write inside it is rolled back.
            T value = await operation();

            await transaction.CommitAsync();

            return value;
        }
    }
}
