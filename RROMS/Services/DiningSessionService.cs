using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 2 (Tables & Reservations)
    // Minimal read/claim API for Member 3 (ordering).
    public class DiningSessionService : IDiningSessionService
    {
        private readonly ApplicationDbContext _db;

        public DiningSessionService(ApplicationDbContext db) => _db = db;

        public Task<DiningSession?> GetOpenSessionForTableAsync(int tableId) =>
            _db.DiningSessions
               .AsNoTracking()
               .FirstOrDefaultAsync(s => s.TableId == tableId && s.Status == DiningSessionStatus.Open);

        public async Task<List<OpenSessionTableDto>> GetOpenSessionTablesAsync() =>
            await _db.DiningSessions
                .AsNoTracking()
                .Where(s => s.Status == DiningSessionStatus.Open)
                .OrderBy(s => s.Table.TableNumber)
                .Select(s => new OpenSessionTableDto(
                    s.Id, s.TableId, s.Table.TableNumber, s.OpenedAt, s.CustomerId != null))
                .ToListAsync();

        public async Task<SessionClaimResult> ClaimOrValidateSessionAsync(int sessionId, string userId)
        {
            var session = await _db.DiningSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                return new(false, "not_found", "This table session does not exist.");
            if (session.Status != DiningSessionStatus.Open)
                return new(false, "closed", "This table session has already ended.");
            if (session.CustomerId == userId)
                return new(true, "ok", "OK");
            if (session.CustomerId != null)
                return new(false, "taken", "This table already belongs to another guest.");

            // Walk-in: claim it atomically so two people cannot both win.
            var rows = await _db.DiningSessions
                .Where(s => s.Id == sessionId
                            && s.CustomerId == null
                            && s.Status == DiningSessionStatus.Open)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.CustomerId, userId));

            if (rows == 1)
                return new(true, "claimed", "OK");

            // Lost the race: check who won.
            var after = await _db.DiningSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => s.CustomerId)
                .FirstOrDefaultAsync();
            return after == userId
                ? new(true, "ok", "OK")
                : new(false, "taken", "This table already belongs to another guest.");
        }
    }
}