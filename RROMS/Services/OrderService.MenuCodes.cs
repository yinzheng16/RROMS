using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;

namespace RROMS.Services
{
    public partial class OrderService
    {
        // Call only inside the existing serializable write transaction. The
        // update/range locks cover absent prefixes as well as existing rows.
        private async Task<string> AllocateCategoryCodeAsync(string name, CancellationToken token)
        {
            var prefixes = await _context.MenuCodeSequences
                .FromSqlRaw("SELECT [Prefix], [LastItemNumber] FROM [MenuCodeSequences] WITH (UPDLOCK, HOLDLOCK)")
                .AsNoTracking().Select(row => row.Prefix).ToListAsync(token);
            string code = MenuCodeRules.CategoryCode(name, prefixes);
            _context.MenuCodeSequences.Add(new MenuCodeSequence { Prefix = code });
            return code;
        }

        private async Task<string> AllocateItemCodeAsync(string prefix, CancellationToken token)
        {
            var sequence = await _context.MenuCodeSequences
                .FromSqlInterpolated($"SELECT [Prefix], [LastItemNumber] FROM [MenuCodeSequences] WITH (UPDLOCK, HOLDLOCK) WHERE [Prefix] = {prefix}")
                .SingleAsync(token);
            var next = MenuCodeRules.NextItemCode(prefix, sequence.LastItemNumber);
            sequence.LastItemNumber = next.Number;
            return next.Code;
        }

        private void DetachMenuCodeSequences()
        {
            // In particular, discard increments rolled back after a deadlock,
            // failed upload, validation failure or unsuccessful commit.
            foreach (var entry in _context.ChangeTracker.Entries<MenuCodeSequence>().ToList())
                entry.State = EntityState.Detached;
        }
    }
}
