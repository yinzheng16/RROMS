// Owner: Member 2 — US16 (staff "Today's reservations" page).
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public class StaffReservationService(
    ApplicationDbContext db,
    IReservationService reservations,
    IProfileService profiles,
    IDiningSessionService sessions,
    INotificationService notifications,
    ILogger<StaffReservationService> logger) : IStaffReservationService
{
    // ---------- the day ----------

    public async Task<StaffDayBoard> GetDayBoardAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        // Same rule as the customer pages: stale bookings are released before anyone looks at them.
        await reservations.ReleaseOverdueReservationsAsync();

        var day = date.Date;
        var next = day.AddDays(1);
        var now = DateTime.Now;

        var raw = await db.Reservations.AsNoTracking()
            .Where(r => r.ReservedAt >= day && r.ReservedAt < next)
            .OrderBy(r => r.ReservedAt).ThenBy(r => r.Id)
            .Select(r => new RawRow
            {
                Id = r.Id,
                ReferenceNo = r.ReferenceNo,
                Status = r.Status,
                ReservedAt = r.ReservedAt,
                DurationMinutes = r.DurationMinutes,
                Guests = r.Guests,
                GuestName = r.User.FullName,
                Phone = r.User.PhoneNumber,
                TableId = r.TableId,
                TableNumber = r.Table != null ? r.Table.TableNumber : null,
                AreaName = r.Table != null ? r.Table.DiningArea.Name : null,
                TableTypeName = r.Table != null && r.Table.TableTypeDefinition != null
                    ? r.Table.TableTypeDefinition.Name : null,
                SpecialRequest = r.SpecialRequest,
                MinimumSpend = r.MinimumSpendAccepted ?? 0m,
                MinimumSpendAccepted = r.MinimumSpendAcceptedAt != null,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var rows = raw.Select(x => ToRow(x, now)).ToList();

        var counts = new StaffDayCounts(
            All: rows.Count,
            Pending: rows.Count(r => r.Status == ReservationStatus.Pending),
            Confirmed: rows.Count(r => r.Status == ReservationStatus.Confirmed),
            Seated: rows.Count(r => r.Status == ReservationStatus.Seated),
            Late: rows.Count(r => r.IsLate),
            Completed: rows.Count(r => r.Status == ReservationStatus.Completed),
            Cancelled: rows.Count(r => r.Status is ReservationStatus.Cancelled or ReservationStatus.NoShow),
            ActiveBookings: rows.Count(r => IsActive(r.Status)),
            ActiveGuests: rows.Where(r => IsActive(r.Status)).Sum(r => r.Guests));

        var isToday = day == now.Date;
        var nextCancel = isToday
            ? rows.Where(r => r.Status == ReservationStatus.Pending && r.AutoCancelAt > now)
                  .Select(r => (DateTime?)r.AutoCancelAt).OrderBy(t => t).FirstOrDefault()
            : null;

        // Pending bookings on ANY upcoming day: staff must not miss one just because another day is open.
        var holdCutoff = now.AddMinutes(-BookingRules.NoShowGraceMinutes);
        var pendingDays = (await db.Reservations.AsNoTracking()
                .Where(r => r.Status == ReservationStatus.Pending && r.ReservedAt >= holdCutoff)
                .GroupBy(r => r.ReservedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .Take(60)
                .ToListAsync(cancellationToken))
            .Select(x => new StaffPendingDay(x.Date, x.Count))
            .ToList();

        return new StaffDayBoard(
            day, now, isToday, day < now.Date,
            await LoadHoursAsync(day, cancellationToken),
            rows,
            await LoadLanesAsync(rows, cancellationToken),
            counts, nextCancel, pendingDays);
    }

    private static bool IsActive(ReservationStatus s) =>
        s is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated;

    private sealed class RawRow
    {
        public int Id { get; set; }
        public string ReferenceNo { get; set; } = "";
        public ReservationStatus Status { get; set; }
        public DateTime ReservedAt { get; set; }
        public int DurationMinutes { get; set; }
        public int Guests { get; set; }
        public string? GuestName { get; set; }
        public string? Phone { get; set; }
        public int? TableId { get; set; }
        public string? TableNumber { get; set; }
        public string? AreaName { get; set; }
        public string? TableTypeName { get; set; }
        public string? SpecialRequest { get; set; }
        public decimal MinimumSpend { get; set; }
        public bool MinimumSpendAccepted { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private static StaffReservationRow ToRow(RawRow x, DateTime now)
    {
        var autoCancelAt = x.ReservedAt.AddMinutes(BookingRules.NoShowGraceMinutes);
        var waiting = x.Status is ReservationStatus.Pending or ReservationStatus.Confirmed;
        var late = waiting && x.ReservedAt <= now;
        var duration = x.DurationMinutes > 0 ? x.DurationMinutes : BookingRules.DurationMinutes;

        int? left = x.Status == ReservationStatus.Pending
            ? Math.Max(0, (int)Math.Ceiling((autoCancelAt - now).TotalMinutes))
            : null;

        return new StaffReservationRow(
            x.Id, x.ReferenceNo, x.Status, x.ReservedAt, x.ReservedAt.AddMinutes(duration),
            x.Guests,
            string.IsNullOrWhiteSpace(x.GuestName) ? "Guest" : x.GuestName!,
            x.Phone, x.TableId, x.TableNumber, x.AreaName, x.TableTypeName,
            string.IsNullOrWhiteSpace(x.SpecialRequest) ? null : x.SpecialRequest!.Trim(),
            x.MinimumSpend, x.MinimumSpendAccepted, x.CreatedAt, autoCancelAt,
            late, late ? Math.Max(0, (int)(now - x.ReservedAt).TotalMinutes) : 0,
            left,
            CanConfirm: x.Status == ReservationStatus.Pending && now < autoCancelAt && x.TableId != null,
            CanSeat: waiting && x.TableId != null && x.ReservedAt.Date == now.Date,
            CanNoShow: waiting && x.ReservedAt <= now,
            CanCancel: waiting,
            CanModify: waiting,
            CanComplete: x.Status == ReservationStatus.Seated);
    }

    // ---------- opening hours (for the timeline) ----------

    private async Task<StaffDayHours> LoadHoursAsync(DateTime day, CancellationToken ct)
    {
        var special = await db.SpecialDates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Date <= day && (s.EndDate ?? s.Date) >= day, ct);

        if (special != null)
        {
            if (special.IsClosed || !special.OpenTime.HasValue || !special.CloseTime.HasValue)
                return new StaffDayHours(true, $"Closed for {special.Name}.", null, null, null, null);

            var sOpen = day + special.OpenTime.Value;
            var sClose = day + special.CloseTime.Value;
            if (sClose <= sOpen) sClose = sClose.AddDays(1);
            return new StaffDayHours(false, null, sOpen, sClose, null, null);
        }

        var h = await db.OperatingHours.AsNoTracking().FirstOrDefaultAsync(x => x.Day == day.DayOfWeek, ct);
        if (h == null || h.IsClosed)
            return new StaffDayHours(true, "Closed today.", null, null, null, null);

        var open = day + h.OpenTime.ToTimeSpan();
        var close = day + h.CloseTime.ToTimeSpan();
        if (close <= open) close = close.AddDays(1);

        DateTime? bs = null, be = null;
        if (h.BreakStart.HasValue && h.BreakEnd.HasValue)
        {
            bs = day + h.BreakStart.Value.ToTimeSpan();
            be = day + h.BreakEnd.Value.ToTimeSpan();
            if (be <= bs || bs < open || be > close) bs = be = null;
        }
        return new StaffDayHours(false, null, open, close, bs, be);
    }

    // ---------- one lane per table, grouped by dining area ----------

    private async Task<IReadOnlyList<StaffAreaLanes>> LoadLanesAsync(
        IReadOnlyList<StaffReservationRow> rows, CancellationToken ct)
    {
        var usedIds = rows.Where(r => r.TableId != null).Select(r => r.TableId!.Value).ToHashSet();

        var tables = await db.RestaurantTables.AsNoTracking()
            .Where(t => (t.IsActive && t.DiningArea.IsActive) || usedIds.Contains(t.Id))
            .Select(t => new { t.Id, t.TableNumber, t.Capacity, AreaId = t.DiningAreaId, Area = t.DiningArea.Name,
                               TypeName = t.TableTypeDefinition != null ? t.TableTypeDefinition.Name : null })
            .ToListAsync(ct);

        var lanes = tables
            .GroupBy(t => new { t.AreaId, t.Area })
            .OrderBy(g => g.Key.AreaId)
            .Select(g => new StaffAreaLanes(
                g.Key.Area,
                g.OrderBy(t => NaturalKey(t.TableNumber))
                 .Select(t => new StaffTableLane(t.Id, t.TableNumber, t.Capacity, t.TypeName)).ToList()))
            .ToList();

        // Bookings that have no table yet get their own lane so they are never hidden.
        if (rows.Any(r => r.TableId == null))
            lanes.Add(new StaffAreaLanes("No table yet", new List<StaffTableLane> { new(0, "—", 0) }));

        return lanes;
    }

    // "T2" < "T10": pad every number so plain text ordering works.
    private static string NaturalKey(string s) =>
        Regex.Replace(s ?? "", @"\d+", m => m.Value.PadLeft(6, '0'));

    // ---------- one booking ----------

    public async Task<StaffReservationDetails?> GetDetailsAsync(int reservationId, CancellationToken ct = default)
    {
        var raw = await db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => new RawRow
            {
                Id = r.Id,
                ReferenceNo = r.ReferenceNo,
                Status = r.Status,
                ReservedAt = r.ReservedAt,
                DurationMinutes = r.DurationMinutes,
                Guests = r.Guests,
                GuestName = r.User.FullName,
                Phone = r.User.PhoneNumber,
                TableId = r.TableId,
                TableNumber = r.Table != null ? r.Table.TableNumber : null,
                AreaName = r.Table != null ? r.Table.DiningArea.Name : null,
                TableTypeName = r.Table != null && r.Table.TableTypeDefinition != null
                    ? r.Table.TableTypeDefinition.Name : null,
                SpecialRequest = r.SpecialRequest,
                MinimumSpend = r.MinimumSpendAccepted ?? 0m,
                MinimumSpendAccepted = r.MinimumSpendAcceptedAt != null,
                CreatedAt = r.CreatedAt
            })
            .SingleOrDefaultAsync(ct);
        if (raw == null) return null;

        var owner = await db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => new { r.UserId, r.User.Email })
            .SingleAsync(ct);

        // Read from the profile of the BOOKING's customer, never the logged-in staff member.
        string? dining = null;
        try { dining = (await profiles.GetDiningSummaryAsync(owner.UserId))?.Trim(); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not read dining preferences for reservation {Id}", reservationId); }
        if (string.IsNullOrWhiteSpace(dining)) dining = null;

        var logs = await db.ReservationStatusLogs.AsNoTracking()
            .Where(l => l.ReservationId == reservationId)
            .OrderBy(l => l.ChangedAt).ThenBy(l => l.Id)
            .Select(l => new StaffStatusLogItem(l.ChangedAt, l.FromStatus.ToString(), l.ToStatus.ToString(),
                l.ChangedByRole, l.Note))
            .ToListAsync(ct);

        var confirmedAt = logs.Where(l => l.To == nameof(ReservationStatus.Confirmed))
            .Select(l => (DateTime?)l.At).LastOrDefault();

        return new StaffReservationDetails(ToRow(raw, DateTime.Now), owner.Email, dining, confirmedAt, logs);
    }

    // ---------- Confirm ----------

    public async Task<StaffActionResult> ConfirmAsync(int reservationId, string staffUserId, string role,
        CancellationToken ct = default)
    {
        if (reservationId <= 0 || string.IsNullOrWhiteSpace(staffUserId))
            return new(false, "not_found", "This reservation could not be found.");

        role = role == Roles.Admin ? "Admin" : "Staff";
        var now = DateTime.Now;
        var cutoff = now.AddMinutes(-BookingRules.NoShowGraceMinutes);

        var current = await db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => new { r.Status, r.ReservedAt, r.TableId })
            .SingleOrDefaultAsync(ct);

        if (current == null)
            return new(false, "not_found", "This reservation could not be found.");
        // A double click or a second staff member: the booking is already confirmed, nothing to do.
        if (current.Status == ReservationStatus.Confirmed)
            return new(true, "already_confirmed", "This reservation is already confirmed.");
        if (current.Status != ReservationStatus.Pending)
            return new(false, "status_changed", $"This reservation is now {Describe(current.Status)}. Refresh to see the latest status.");
        if (current.ReservedAt < cutoff)
            return new(false, "expired", "This booking has passed its hold time and will be cancelled automatically.");
        if (current.TableId == null)
            return new(false, "no_table", "This booking has no table yet, so it cannot be confirmed.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // One conditional UPDATE: it only succeeds while the booking is still Pending and inside its hold time.
        // If the customer cancelled or the system released it a moment ago, 0 rows change.
        var changed = await db.Reservations
            .Where(r => r.Id == reservationId && r.Status == ReservationStatus.Pending &&
                        r.ReservedAt >= cutoff && r.TableId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Confirmed), ct);

        if (changed != 1)
        {
            await tx.RollbackAsync(ct);
            var now2 = await db.Reservations.AsNoTracking().Where(r => r.Id == reservationId)
                .Select(r => r.Status).SingleOrDefaultAsync(ct);
            return now2 == ReservationStatus.Confirmed
                ? new(true, "already_confirmed", "This reservation is already confirmed.")
                : new(false, "status_changed", $"This reservation is now {Describe(now2)}. Refresh to see the latest status.");
        }

        db.ReservationStatusLogs.Add(new ReservationStatusLog
        {
            ReservationId = reservationId,
            FromStatus = ReservationStatus.Pending,
            ToStatus = ReservationStatus.Confirmed,
            ChangedByUserId = staffUserId,
            ChangedByRole = role,
            ChangedAt = now,
            Note = "Confirmed by " + role.ToLowerInvariant()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await NotifyConfirmedAsync(reservationId, ct);
        return new(true, "confirmed", "Reservation confirmed. The customer has been notified.");
    }

    private static string Describe(ReservationStatus s) => s switch
    {
        ReservationStatus.NoShow => "marked as a no-show",
        ReservationStatus.Pending => "pending",
        _ => s.ToString().ToLowerInvariant()
    };

    // The booking is already saved. A failed notification must never undo or hide that.
    private async Task NotifyConfirmedAsync(int reservationId, CancellationToken ct)
    {
        try
        {
            var r = await db.Reservations.AsNoTracking()
                .Where(x => x.Id == reservationId)
                .Select(x => new { x.UserId, x.ReferenceNo, x.ReservedAt, x.Guests })
                .SingleOrDefaultAsync(ct);
            if (r == null) return;

            var when = r.ReservedAt.ToString("ddd, d MMM", CultureInfo.InvariantCulture) + " at " +
                       r.ReservedAt.ToString("h:mm tt", CultureInfo.InvariantCulture);

            await notifications.SendAsync(
                r.UserId,
                "Reservation confirmed",
                $"Your table for {r.Guests} on {when} is confirmed ({r.ReferenceNo}). See My Reservations for your table.",
                NotificationType.Reservation);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reservation {Id} was confirmed but the customer notification failed", reservationId);
        }
    }

    // ====================================================================== US17

    // Gap kept between two bookings on the same table. Same value as BookingRules (15 minutes).
    private const int TurnoverMinutes = 15;

    private static string RoleName(string role) => role == Roles.Admin ? "Admin" : "Staff";

    private static string Sentence(ReservationStatus s) => Describe(s);

    // ---------- Seat (opens the dining session) ----------

    public async Task<StaffActionResult> SeatAsync(int reservationId, string staffUserId, string role,
        CancellationToken ct = default)
    {
        if (reservationId <= 0 || string.IsNullOrWhiteSpace(staffUserId))
            return new(false, "not_found", "This reservation could not be found.");

        var r = await sessions.OpenSessionForReservationAsync(reservationId, staffUserId, RoleName(role), ct);
        if (!r.Success) return new(false, r.Code, r.Message);
        if (r.Code == "already_seated") return new(true, "already_seated", r.Message);

        await NotifyAsync(reservationId, "Welcome to YORU",
            "You are seated at table {table}. You can now order from your table in the app.", ct);

        var extra = r.AttachedOrders > 0
            ? $" {r.AttachedOrders} earlier order{(r.AttachedOrders == 1 ? " was" : "s were")} added to the table."
            : "";
        return new(true, "seated", $"Seated at table {r.TableNumber}. Dining session opened.{extra}");
    }

    // ---------- Complete (guests left) ----------

    public async Task<StaffActionResult> CompleteAsync(int reservationId, string staffUserId, string role,
        CancellationToken ct = default)
    {
        if (reservationId <= 0 || string.IsNullOrWhiteSpace(staffUserId))
            return new(false, "not_found", "This reservation could not be found.");

        var cur = await db.Reservations.AsNoTracking().Where(r => r.Id == reservationId)
            .Select(r => new { r.Status }).SingleOrDefaultAsync(ct);
        if (cur == null) return new(false, "not_found", "This reservation could not be found.");
        if (cur.Status == ReservationStatus.Completed)
            return new(true, "already_completed", "This visit is already completed.");
        if (cur.Status != ReservationStatus.Seated)
            return new(false, "status_changed", $"This reservation is now {Sentence(cur.Status)}. Only seated guests can be completed.");

        var session = await sessions.GetOpenSessionForReservationAsync(reservationId, ct);
        if (session != null)
        {
            var closed = await sessions.CloseSessionAsync(session.Id, staffUserId, RoleName(role), ct);
            if (!closed.Success) return new(false, closed.Code, closed.Message);
        }
        else
        {
            // Seated but no open session (should not happen): still finish the booking.
            var changed = await db.Reservations
                .Where(r => r.Id == reservationId && r.Status == ReservationStatus.Seated)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Completed), ct);
            if (changed == 1)
            {
                db.ReservationStatusLogs.Add(NewLog(reservationId, ReservationStatus.Seated, ReservationStatus.Completed,
                    staffUserId, role, "Guests left, completed by " + RoleName(role).ToLowerInvariant()));
                await db.SaveChangesAsync(ct);
            }
        }

        await NotifyAsync(reservationId, "Thank you for dining with us",
            "We hope you enjoyed your meal at YORU Izakaya. See you again soon!", ct);
        return new(true, "completed", "Guests have left. Reservation completed and the table is free.");
    }

    // ---------- No-show ----------

    public async Task<StaffActionResult> NoShowAsync(int reservationId, string staffUserId, string role,
        CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var res = await MoveAsync(reservationId, staffUserId, role, ReservationStatus.NoShow,
            "Marked as no-show by " + RoleName(role).ToLowerInvariant(),
            (cur) => cur.ReservedAt > now ? "The booking time has not been reached yet. Use Cancel booking instead." : null,
            ct);
        if (res.Success && res.Code == "done")
        {
            await NotifyAsync(reservationId, "Reservation marked as missed",
                "You did not arrive for your reservation {ref} on {when}, so the table was released.", ct);
            return new(true, "noshow", "Marked as no-show. The table is free again.");
        }
        return res.Success ? new(true, "already_noshow", "This booking is already marked as a no-show.") : res;
    }

    // ---------- Cancel by staff ----------

    public async Task<StaffActionResult> CancelAsync(int reservationId, string staffUserId, string role,
        string? reason, CancellationToken ct = default)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is { Length: > 200 }) reason = reason[..200];

        var res = await MoveAsync(reservationId, staffUserId, role, ReservationStatus.Cancelled,
            "Cancelled by " + RoleName(role).ToLowerInvariant() + (reason != null ? ": " + reason : ""),
            null, ct);
        if (res.Success && res.Code == "done")
        {
            await NotifyAsync(reservationId, "Reservation cancelled",
                "Your reservation {ref} on {when} was cancelled by the restaurant." +
                (reason != null ? " Reason: " + reason : "") + " Please contact us or book another time.", ct);
            return new(true, "cancelled", "Booking cancelled. The customer has been notified.");
        }
        return res.Success ? new(true, "already_cancelled", "This booking is already cancelled.") : res;
    }

    // Shared by No-show and Cancel: Pending / Confirmed -> target, one conditional UPDATE + one history row.
    private async Task<StaffActionResult> MoveAsync(int reservationId, string staffUserId, string role,
        ReservationStatus target, string note, Func<CurrentRes, string?>? extraGuard, CancellationToken ct)
    {
        if (reservationId <= 0 || string.IsNullOrWhiteSpace(staffUserId))
            return new(false, "not_found", "This reservation could not be found.");

        var cur = await db.Reservations.AsNoTracking().Where(r => r.Id == reservationId)
            .Select(r => new CurrentRes(r.Status, r.ReservedAt, r.TableId)).SingleOrDefaultAsync(ct);
        if (cur == null) return new(false, "not_found", "This reservation could not be found.");
        if (cur.Status == target) return new(true, "already", "Nothing to change.");
        if (cur.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
            return new(false, "status_changed", $"This reservation is now {Sentence(cur.Status)}. Refresh to see the latest status.");
        var blocked = extraGuard?.Invoke(cur);
        if (blocked != null) return new(false, "too_early", blocked);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var from = cur.Status;
        var changed = await db.Reservations
            .Where(r => r.Id == reservationId && r.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, target), ct);

        if (changed != 1)
        {
            await tx.RollbackAsync(ct);
            var now2 = await db.Reservations.AsNoTracking().Where(r => r.Id == reservationId)
                .Select(r => r.Status).SingleOrDefaultAsync(ct);
            return now2 == target
                ? new(true, "already", "Nothing to change.")
                : new(false, "status_changed", $"This reservation is now {Sentence(now2)}. Refresh to see the latest status.");
        }

        db.ReservationStatusLogs.Add(NewLog(reservationId, from, target, staffUserId, role, note));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, "done", "OK");
    }

    private sealed record CurrentRes(ReservationStatus Status, DateTime ReservedAt, int? TableId);

    private static ReservationStatusLog NewLog(int id, ReservationStatus from, ReservationStatus to,
        string userId, string role, string note) => new()
    {
        ReservationId = id,
        FromStatus = from,
        ToStatus = to,
        ChangedByUserId = userId,
        ChangedByRole = RoleName(role),
        ChangedAt = DateTime.Now,
        Note = note.Length > 250 ? note[..250] : note
    };

    // ---------- Modify ----------

    public async Task<StaffEditOptions?> GetEditOptionsAsync(int reservationId, DateTime date, string? time, int guests,
        CancellationToken ct = default)
    {
        var cur = await db.Reservations.AsNoTracking().Where(r => r.Id == reservationId)
            .Select(r => new { r.Status, r.ReservedAt, r.DurationMinutes, r.TableId, r.Guests }).SingleOrDefaultAsync(ct);
        if (cur == null) return null;

        var day = date.Date;
        var hours = await LoadHoursAsync(day, ct);
        if (hours.IsClosed || hours.Open == null || hours.Close == null)
            return new StaffEditOptions(true, hours.ClosedReason ?? "Closed on this day.", [], "", []);

        // every 15 minutes from opening until 30 minutes before closing, skipping the afternoon break
        var slots = new List<StaffSlot>();
        for (var t = hours.Open.Value; t <= hours.Close.Value.AddMinutes(-30); t = t.AddMinutes(15))
        {
            if (hours.BreakStart != null && t >= hours.BreakStart && t < hours.BreakEnd) continue;
            slots.Add(new StaffSlot(t.ToString("HH:mm", CultureInfo.InvariantCulture),
                t.ToString("h:mm tt", CultureInfo.InvariantCulture)));
        }

        // keep the current time selected when it is still a valid slot, otherwise the first one
        var wanted = !string.IsNullOrWhiteSpace(time) ? time!
            : cur.ReservedAt.Date == day ? cur.ReservedAt.ToString("HH:mm", CultureInfo.InvariantCulture) : "";
        var chosen = slots.FirstOrDefault(s => s.Value == wanted)?.Value ?? slots.FirstOrDefault()?.Value ?? "";
        if (chosen == "") return new StaffEditOptions(true, "No bookable times on this day.", slots, "", []);

        var start = day + TimeSpan.ParseExact(chosen, "hh\\:mm", CultureInfo.InvariantCulture);
        var duration = cur.DurationMinutes > 0 ? cur.DurationMinutes : BookingRules.DurationMinutes;
        guests = Math.Max(1, guests);

        var tables = await db.RestaurantTables.AsNoTracking()
            .Where(t => (t.IsActive && t.DiningArea.IsActive) || t.Id == cur.TableId)
            .Select(t => new
            {
                t.Id, t.TableNumber, t.Capacity, t.MinGuests,
                Area = t.DiningArea.Name,
                Active = t.IsActive && t.DiningArea.IsActive
            })
            .ToListAsync(ct);

        var busy = await BusyTablesAsync(reservationId, start, duration, ct);

        var list = tables
            .OrderBy(t => t.Area).ThenBy(t => NaturalKey(t.TableNumber))
            .Select(t =>
            {
                string? note = null;
                if (!t.Active) note = "not in use";
                else if (t.Capacity < guests) note = "too small";
                else if (t.MinGuests > guests) note = $"min {t.MinGuests} guests";
                else if (busy.Contains(t.Id)) note = "taken at this time";
                return new StaffTableOption(t.Id, t.TableNumber, t.Area, t.Capacity, note == null, note);
            })
            .ToList();

        return new StaffEditOptions(false, null, slots, chosen, list);
    }

    // Tables that cannot take this booking at this time: another active booking too close, or guests still seated.
    private async Task<HashSet<int>> BusyTablesAsync(int ignoreReservationId, DateTime start, int duration,
        CancellationToken ct)
    {
        var end = start.AddMinutes(duration);
        var from = start.AddHours(-12);
        var to = end.AddHours(12);

        var near = await db.Reservations.AsNoTracking()
            .Where(r => r.Id != ignoreReservationId && r.TableId != null &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Seated) &&
                        r.ReservedAt >= from && r.ReservedAt <= to)
            .Select(r => new { TableId = r.TableId!.Value, r.ReservedAt, r.DurationMinutes })
            .ToListAsync(ct);

        var busy = near
            .Where(r =>
            {
                var rEnd = r.ReservedAt.AddMinutes(r.DurationMinutes > 0 ? r.DurationMinutes : BookingRules.DurationMinutes);
                return r.ReservedAt < end.AddMinutes(TurnoverMinutes) && rEnd.AddMinutes(TurnoverMinutes) > start;
            })
            .Select(r => r.TableId)
            .ToHashSet();

        // Guests of ANOTHER booking sitting right now (and still sitting when this one starts).
        var now = DateTime.Now;
        if (start < now.AddMinutes(duration))
        {
            var open = await db.DiningSessions.AsNoTracking()
                .Where(s => s.Status == DiningSessionStatus.Open && s.ReservationId != ignoreReservationId)
                .Select(s => s.TableId).ToListAsync(ct);
            foreach (var id in open) busy.Add(id);
        }
        return busy;
    }

    public async Task<StaffActionResult> ModifyAsync(int reservationId, StaffModifyRequest req, string staffUserId,
        string role, CancellationToken ct = default)
    {
        if (reservationId <= 0 || string.IsNullOrWhiteSpace(staffUserId))
            return new(false, "not_found", "This reservation could not be found.");

        var cur = await db.Reservations.AsNoTracking().Where(r => r.Id == reservationId)
            .Select(r => new
            {
                r.Status, r.ReservedAt, r.DurationMinutes, r.Guests, r.TableId, r.SpecialRequest,
                TableNumber = r.Table != null ? r.Table.TableNumber : null
            }).SingleOrDefaultAsync(ct);
        if (cur == null) return new(false, "not_found", "This reservation could not be found.");
        if (cur.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
            return new(false, "status_changed", $"This reservation is now {Sentence(cur.Status)} and can no longer be changed.");

        // Special request: trimmed, blank = none, same 500-character limit as the customer's booking form.
        var newRequest = (req.SpecialRequest ?? "").Trim();
        if (newRequest.Length > 500) newRequest = newRequest[..500];
        string? requestValue = newRequest.Length == 0 ? null : newRequest;
        var requestChanged = (cur.SpecialRequest ?? "").Trim() != newRequest;

        if (!TimeSpan.TryParseExact(req.Time ?? "", "hh\\:mm", CultureInfo.InvariantCulture, out var tod))
            return new(false, "bad_time", "Choose a valid time.");
        var start = req.Date.Date + tod;
        var guests = req.Guests;
        if (guests < 1 || guests > 50)
            return new(false, "bad_guests", "Enter a number of guests between 1 and 50.");

        var timeChanged = start != cur.ReservedAt;
        if (timeChanged && start < DateTime.Now)
            return new(false, "past", "The new time has already passed.");

        var hours = await LoadHoursAsync(start.Date, ct);
        if (timeChanged)
        {
            if (hours.IsClosed || hours.Open == null || hours.Close == null)
                return new(false, "closed", hours.ClosedReason ?? "The restaurant is closed on this day.");
            if (start < hours.Open || start > hours.Close.Value.AddMinutes(-30))
                return new(false, "outside_hours", "That time is outside opening hours.");
            if (hours.BreakStart != null && start >= hours.BreakStart && start < hours.BreakEnd)
                return new(false, "break", "That time is during the afternoon break.");
        }

        var table = await db.RestaurantTables.AsNoTracking().Where(t => t.Id == req.TableId)
            .Select(t => new { t.Id, t.TableNumber, t.Capacity, t.MinGuests, Area = t.DiningArea.Name,
                Active = t.IsActive && t.DiningArea.IsActive }).SingleOrDefaultAsync(ct);
        if (table == null) return new(false, "bad_table", "Choose a table.");
        var tableChanged = table.Id != cur.TableId;
        if (tableChanged && !table.Active) return new(false, "bad_table", $"Table {table.TableNumber} is not in use.");
        if (guests > table.Capacity) return new(false, "capacity", $"Table {table.TableNumber} seats {table.Capacity} guests.");
        if (guests < table.MinGuests) return new(false, "min_guests", $"Table {table.TableNumber} needs at least {table.MinGuests} guests.");

        if (!timeChanged && !tableChanged && !requestChanged && guests == cur.Guests)
            return new(true, "unchanged", "Nothing was changed.");

        var duration = cur.DurationMinutes > 0 ? cur.DurationMinutes : BookingRules.DurationMinutes;
        var from = cur.Status;

        // Serializable: the availability check and the update cannot be interleaved with another booking.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        if ((timeChanged || tableChanged) && (await BusyTablesAsync(reservationId, start, duration, ct)).Contains(table.Id))
        {
            await tx.RollbackAsync(ct);
            return new(false, "table_taken", $"Table {table.TableNumber} is not free at {start:h:mm tt}. Pick another table or time.");
        }

        var changed = await db.Reservations
            .Where(r => r.Id == reservationId && r.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ReservedAt, start)
                .SetProperty(r => r.Guests, guests)
                .SetProperty(r => r.TableId, table.Id)
                .SetProperty(r => r.SpecialRequest, requestValue), ct);

        if (changed != 1)
        {
            await tx.RollbackAsync(ct);
            return new(false, "status_changed", "This reservation was changed by someone else. Refresh and try again.");
        }

        var parts = new List<string>();
        if (timeChanged) parts.Add($"time {cur.ReservedAt:d MMM, h:mm tt} -> {start:d MMM, h:mm tt}");
        if (guests != cur.Guests) parts.Add($"guests {cur.Guests} -> {guests}");
        if (tableChanged) parts.Add($"table {cur.TableNumber ?? "none"} -> {table.TableNumber}");
        if (requestChanged) parts.Add(requestValue == null ? "special request removed" : "special request updated");
        var summary = string.Join("; ", parts);

        db.ReservationStatusLogs.Add(NewLog(reservationId, from, from, staffUserId, role,
            "Modified by " + RoleName(role).ToLowerInvariant() + ": " + summary));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await NotifyAsync(reservationId, "Reservation updated",
            "Your reservation {ref} was updated by the restaurant: " + summary.Replace(" -> ", " to ") +
            ". It is now on {when} for {guests}.", ct);

        return new(true, "modified", "Reservation updated. The customer has been notified.");
    }

    // ---------- notification (never undoes the saved change) ----------

    // {ref} {when} {guests} {table} are filled from the booking as it is NOW (after the change).
    // In-app notification only: emails are sent by the notification service (Member 4).
    private async Task NotifyAsync(int reservationId, string title, string message, CancellationToken ct)
    {
        try
        {
            var r = await db.Reservations.AsNoTracking()
                .Where(x => x.Id == reservationId)
                .Select(x => new
                {
                    x.UserId, x.ReferenceNo, x.ReservedAt, x.Guests,
                    Table = x.Table != null ? x.Table.TableNumber : ""
                })
                .SingleOrDefaultAsync(ct);
            if (r == null) return;

            var when = r.ReservedAt.ToString("ddd, d MMM", CultureInfo.InvariantCulture) + " at " +
                       r.ReservedAt.ToString("h:mm tt", CultureInfo.InvariantCulture);
            var text = message.Replace("{ref}", r.ReferenceNo).Replace("{when}", when)
                .Replace("{guests}", r.Guests == 1 ? "1 guest" : r.Guests + " guests").Replace("{table}", r.Table);

            await notifications.SendAsync(r.UserId, title, text, NotificationType.Reservation);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reservation {Id} was updated but the customer notification failed", reservationId);
        }
    }
}