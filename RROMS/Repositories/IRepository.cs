using System.Linq.Expressions;

namespace RROMS.Repositories
{
    // Generic data-access contract used by the service layer.
    // Services depend on this interface, not on ApplicationDbContext directly.
    public interface IRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(int id);
        Task<List<T>> GetAllAsync();
        Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate);
        IQueryable<T> Query();   // for Include(), paging and sorting in services
        Task AddAsync(T entity);
        void Update(T entity);
        void Remove(T entity);
        Task<int> SaveChangesAsync();
    }
}
