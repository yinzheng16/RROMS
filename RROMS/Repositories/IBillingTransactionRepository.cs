namespace RROMS.Repositories
{
    public interface IBillingTransactionRepository
    {
        Task<T> ExecuteForTakeawayOrderAsync<T>(
            int orderId,
            Func<Task<T>> operation);
    }
}