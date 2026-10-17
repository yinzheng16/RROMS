using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;

namespace RROMS.Repositories
{
    // Default EF Core implementation of IRepository<T>.
    // Create a specific repository (e.g. ReservationRepository : Repository<Reservation>)
    // only when your module needs extra queries.
    public class Repository<T> : IRepository<T> where T : class
    {
        protected readonly ApplicationDbContext Db;
        protected readonly DbSet<T> Set;

        public Repository(ApplicationDbContext db)
        {
            Db = db;
            Set = db.Set<T>();
        }

        public async Task<T?> GetByIdAsync(int id) => await Set.FindAsync(id);

        public async Task<List<T>> GetAllAsync() => await Set.AsNoTracking().ToListAsync();

        public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
            await Set.AsNoTracking().Where(predicate).ToListAsync();

        public IQueryable<T> Query() => Set.AsQueryable();

        public async Task AddAsync(T entity) => await Set.AddAsync(entity);

        public void Update(T entity) => Set.Update(entity);

        public void Remove(T entity) => Set.Remove(entity);

        public Task<int> SaveChangesAsync() => Db.SaveChangesAsync();
    }
}
