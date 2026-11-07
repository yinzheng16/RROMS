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

            // A session without an owner (only old data has one): claim it atomically so two people cannot both win.
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

        // ================================================================ US17

        public Task<DiningSession?> GetOpenSessionForReservationAsync(int reservationId, CancellationToken ct = default) =>
            _db.DiningSessions.AsNoTracking()
               .FirstOrDefaultAsync(s => s.ReservationId == reservationId && s.Status == DiningSessionStatus.Open, ct);

        public async Task<SessionOpenResult> OpenSessionForReservationAsync(
            int reservationId, string changedByUserId, string role, CancellationToken ct = default)
        {
            role = role == "Admin" ? "Admin" : "Staff";
            var now = DateTime.Now;

            var cur = await _db.Reservations.AsNoTracking()
                .Where(r => r.Id == reservationId)
                .Select(r => new
                {
                    r.Status,
                    r.ReservedAt,
                    r.TableId,
                    r.UserId,
                    TableNumber = r.Table != null ? r.Table.TableNumber : null
                })
                .SingleOrDefaultAsync(ct);

            if (cur == null)
                return new(false, "not_found", "This reservation could not be found.");

            if (cur.Status == ReservationStatus.Seated)
            {
                var existing = await GetOpenSessionForReservationAsync(reservationId, ct);
                return new(true, "already_seated", "These guests are already seated.", existing?.Id, 0, cur.TableNumber);
            }
            if (cur.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
                return new(false, "status_changed",
                    $"This reservation is now {Plain(cur.Status)}. Refresh to see the latest status.");
            if (cur.TableId == null)
                return new(false, "no_table", "This booking has no table yet, so guests cannot be seated.");
            if (cur.ReservedAt.Date != now.Date)
                return new(false, "wrong_day", "Only a booking for today can be marked as seated.");
            if (cur.ReservedAt > now.AddMinutes(BookingRules.SeatEarlyMinutes))
                return new(false, "too_early",
                    $"Guests can be seated from {cur.ReservedAt.AddMinutes(-BookingRules.SeatEarlyMinutes).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}, at most 1 hour before the booking time.");

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // The table must be free: a previous party's session has to be closed first.
            var busy = await _db.DiningSessions.AsNoTracking()
                .AnyAsync(s => s.TableId == cur.TableId && s.Status == DiningSessionStatus.Open
                               && s.ReservationId != reservationId, ct);
            if (busy)
            {
                await tx.RollbackAsync(ct);
                return new(false, "table_busy",
                    $"Table {cur.TableNumber} still has an open session from an earlier party. Close it first (guests left).");
            }

            // One conditional UPDATE: only works while the booking is still Pending / Confirmed.
            var changed = await _db.Reservations
                .Where(r => r.Id == reservationId && r.TableId != null &&
                            (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, ReservationStatus.Seated), ct);

            if (changed != 1)
            {
                await tx.RollbackAsync(ct);
                var now2 = await _db.Reservations.AsNoTracking().Where(r => r.Id == reservationId)
                    .Select(r => r.Status).SingleOrDefaultAsync(ct);
                return now2 == ReservationStatus.Seated
                    ? new(true, "already_seated", "These guests are already seated.", null, 0, cur.TableNumber)
                    : new(false, "status_changed", $"This reservation is now {Plain(now2)}. Refresh to see the latest status.");
            }

            var session = new DiningSession
            {
                TableId = cur.TableId.Value,
                ReservationId = reservationId,
                CustomerId = cur.UserId,
                OpenedAt = now,
                Status = DiningSessionStatus.Open
            };
            _db.DiningSessions.Add(session);

            _db.ReservationStatusLogs.Add(new ReservationStatusLog
            {
                ReservationId = reservationId,
                FromStatus = cur.Status,
                ToStatus = ReservationStatus.Seated,
                ChangedByUserId = changedByUserId,
                ChangedByRole = role,
                ChangedAt = now,
                Note = "Guests seated by " + role.ToLowerInvariant()
            });
            await _db.SaveChangesAsync(ct);

            var attached = await AttachPreArrivalOrdersAsync(session.Id, reservationId, ct);

            await tx.CommitAsync(ct);
            return new(true, "opened", "Guests seated. The table is open for ordering.", session.Id, attached, cur.TableNumber);
        }

        // ----------------------------------------------------------------------------------------
        // Orders the customer placed for this reservation BEFORE arriving have no session yet.
        // When the guests are seated they join the new session so the table has one bill.
        //
        // ASSUMPTION (Member 3's Order model): Order has ReservationId (int?), DiningSessionId (int?)
        // and BillId (int?). Only orders of this reservation that have no session and are not billed
        // are attached. If your property names differ, change ONLY the three names in this method.
        // ----------------------------------------------------------------------------------------
        private Task<int> AttachPreArrivalOrdersAsync(int sessionId, int reservationId, CancellationToken ct) =>
            _db.Orders
               .Where(o => o.ReservationId == reservationId && o.DiningSessionId == null && o.BillId == null)
               .ExecuteUpdateAsync(u => u.SetProperty(o => o.DiningSessionId, sessionId), ct);

        public async Task<SessionCloseResult> CloseSessionAsync(
            int sessionId, string? changedByUserId = null, string role = "System", CancellationToken ct = default)
        {
            role = role switch { "Admin" => "Admin", "Staff" => "Staff", _ => "System" };
            var now = DateTime.Now;

            var s = await _db.DiningSessions.AsNoTracking()
                .Where(x => x.Id == sessionId)
                .Select(x => new { x.Status, x.ReservationId, TableNumber = x.Table.TableNumber })
                .SingleOrDefaultAsync(ct);

            if (s == null)
                return new(false, "not_found", "This table session could not be found.");
            if (s.Status != DiningSessionStatus.Open)
                return new(true, "already_closed", "This table session has already ended.", s.ReservationId, s.TableNumber);

            // Use the caller's transaction when there is one; otherwise make our own.
            var own = _db.Database.CurrentTransaction == null;
            var tx = own ? await _db.Database.BeginTransactionAsync(ct) : null;
            try
            {
                var closed = await _db.DiningSessions
                    .Where(x => x.Id == sessionId && x.Status == DiningSessionStatus.Open)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.Status, DiningSessionStatus.Closed)
                        .SetProperty(x => x.ClosedAt, now), ct);

                if (closed != 1)
                {
                    if (tx != null) await tx.RollbackAsync(ct);
                    return new(true, "already_closed", "This table session has already ended.", s.ReservationId, s.TableNumber);
                }

                if (s.ReservationId != null)
                {
                    var rid = s.ReservationId.Value;
                    var done = await _db.Reservations
                        .Where(r => r.Id == rid && r.Status == ReservationStatus.Seated)
                        .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, ReservationStatus.Completed), ct);
                    if (done == 1)
                    {
                        _db.ReservationStatusLogs.Add(new ReservationStatusLog
                        {
                            ReservationId = rid,
                            FromStatus = ReservationStatus.Seated,
                            ToStatus = ReservationStatus.Completed,
                            ChangedByUserId = changedByUserId,
                            ChangedByRole = role,
                            ChangedAt = now,
                            Note = role == "System" ? "Session closed" : "Guests left, closed by " + role.ToLowerInvariant()
                        });
                        await _db.SaveChangesAsync(ct);
                    }
                }

                if (tx != null) await tx.CommitAsync(ct);
            }
            finally
            {
                if (tx != null) await tx.DisposeAsync();
            }

            return new(true, "closed", "Guests have left. The table is free again.", s.ReservationId, s.TableNumber);
        }

        private static string Plain(ReservationStatus s) =>
            s == ReservationStatus.NoShow ? "marked as a no-show" : s.ToString().ToLowerInvariant();
    }
}
