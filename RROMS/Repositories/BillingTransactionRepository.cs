using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;

namespace RROMS.Repositories
{
    // US32: Provides transaction-level locking for takeaway bill creation.
    public class BillingTransactionRepository : IBillingTransactionRepository
    {
        private readonly ApplicationDbContext _db;

        public BillingTransactionRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<T> ExecuteForTakeawayOrderAsync<T>(
            int orderId,
            Func<Task<T>> operation)
        {
            if (orderId <= 0)
                throw new ArgumentException(
                    "Invalid order ID.", nameof(orderId));

            ArgumentNullException.ThrowIfNull(operation);

            // The lock and bill creation must use the same transaction.
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
            parameter.Value = $"RROMS:TakeawayBill:{orderId}";
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
            {
                throw new InvalidOperationException(
                    $"SQL Server did not return a billing lock result for Order {orderId}.");
            }

            int lockResult = Convert.ToInt32(result);

            if (lockResult < 0)
            {
                throw new InvalidOperationException(
                    $"Could not acquire billing lock for Order {orderId}. " +
                    $"SQL Server lock result: {lockResult}.");
            }

            // Execute the billing operation while holding the lock.
            T value = await operation();

            await transaction.CommitAsync();

            return value;
        }
    }
}
