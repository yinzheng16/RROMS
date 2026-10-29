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
            CanConfirm: x.Status == ReservationStatus.Pending && now < autoCancelAt && x.TableId != null);
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
            .Select(t => new {
                t.Id,
                t.TableNumber,
                t.Capacity,
                AreaId = t.DiningAreaId,
                Area = t.DiningArea.Name,
                TypeName = t.TableTypeDefinition != null ? t.TableTypeDefinition.Name : null
            })
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
}
