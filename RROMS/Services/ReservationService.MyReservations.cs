// Owner: Member 2 — US13. Add alongside your existing partial ReservationService.
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public partial class ReservationService : IMyReservationsService
{
    public async Task<IReadOnlyList<MyReservationItem>> GetMyUpcomingReservationsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<MyReservationItem>();

        // Same local restaurant-time convention as the existing US12 service.
        // Include arrivals within the existing grace period, but do not allow cancellation
        // once ReservedAt is reached. Expired/terminal reservations are not a history tab.
        var now = DateTime.Now;
        var cutoff = now.AddMinutes(-BookingRules.NoShowGraceMinutes);
        var rows = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Table)
                .ThenInclude(t => t!.TableTypeDefinition)
            .Where(r => r.UserId == userId &&
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
                r.ReservedAt >= cutoff)
            .OrderBy(r => r.ReservedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        // Area names are read once (the table only stores the area id).
        var areaIds = rows.Where(r => r.Table != null).Select(r => r.Table!.DiningAreaId).Distinct().ToList();
        var areaNames = await _db.DiningAreas.AsNoTracking()
            .Where(a => areaIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        return rows.Select(r => new MyReservationItem(
            r.Id,
            r.ReferenceNo,
            r.ReservedAt,
            r.Guests,
            r.Table?.TableTypeDefinition?.Name ?? "Table",
            r.Table?.TableTypeDefinition?.JapaneseName,
            r.SpecialRequest,
            r.MinimumSpendAccepted ?? 0m,
            r.MinimumSpendAcceptedAt.HasValue,
            r.Status.ToString(),
            r.ReservedAt > now,
            r.Table != null && areaNames.TryGetValue(r.Table.DiningAreaId, out var area) ? area : null,
            // The customer only sees the table number once staff have confirmed the booking.
            r.Status == ReservationStatus.Confirmed ? r.Table?.TableNumber : null)).ToArray();
    }

    public async Task<MyReservationCancelResult> CancelMyReservationAsync(
        string userId, int reservationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || reservationId <= 0)
            return new(false, "not_found", "This reservation could not be found.");

        var now = DateTime.Now;

        // Which status are we cancelling from? (needed for the history row)
        var from = await _db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId && r.UserId == userId && r.ReservedAt > now &&
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .Select(r => (ReservationStatus?)r.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (from is not null)
        {
            // The status change and its history row are saved together, or not at all.
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            // One conditional SQL UPDATE: ownership, time and the status we just read are checked
            // together. A concurrent staff update to Seated/Completed cannot be overwritten.
            // Requires EF Core 7+ (SQL Server), and deliberately does not reset Table.Status:
            // the existing US12 availability query already excludes Cancelled reservations.
            var changed = await _db.Reservations
                .Where(r => r.Id == reservationId && r.UserId == userId &&
                    r.ReservedAt > now && r.Status == from.Value)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, ReservationStatus.Cancelled), cancellationToken);

            if (changed == 1)
            {
                _db.ReservationStatusLogs.Add(new ReservationStatusLog
                {
                    ReservationId = reservationId,
                    FromStatus = from.Value,
                    ToStatus = ReservationStatus.Cancelled,
                    ChangedByUserId = userId,
                    ChangedByRole = "Customer",
                    ChangedAt = now,
                    Note = "Cancelled by customer"
                });
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return new(true, "cancelled", "Your reservation has been cancelled.");
            }
        }

        var current = await _db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId && r.UserId == userId)
            .Select(r => new { r.Status, r.ReservedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (current == null)
            return new(false, "not_found", "This reservation could not be found.");
        // A duplicate/retried POST is safe, including when the first response was lost.
        if (current.Status == ReservationStatus.Cancelled)
            return new(true, "already_cancelled", "This reservation is already cancelled.");
        if (current.ReservedAt <= DateTime.Now)
            return new(false, "too_late", "You cannot cancel after the scheduled reservation time.");

        return new(false, "status_changed", "This reservation can no longer be cancelled. Please refresh to see its latest status.");
    }

    // ================= US15: change my reservation =================
    // A customer may change the date, time, number of guests or table type of a booking that is still
    // Pending or Confirmed and has not reached its time (the same rule as cancelling). The reference stays.
    //  - Only the number of guests changes, and the party still fits the booked table: nothing else changes,
    //    the booking keeps its table, time and status. (Up to the table's capacity.)
    //  - The date, time or table type changes: a table must be free, the booking must be at least 1 hour
    //    ahead, and the booking goes back to Pending so staff confirm the new details again.

    private static bool CanChange(Reservation r, DateTime now) =>
        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
        r.ReservedAt > now;

    // Why this table type cannot take this party, or null when it can.
    private static string? ChangeTypeProblem(TableTypeDefinition? type, int guests)
    {
        if (type == null || !type.IsActive)
            return "This table type is no longer available.";

        if (guests > type.DefaultMaxGuests ||
            (guests < type.DefaultMinGuests && !type.AllowBelowMinGuests))
        {
            var min = type.AllowBelowMinGuests ? 1 : type.DefaultMinGuests;
            return $"{type.Name} seats {min} to {type.DefaultMaxGuests} guests.";
        }

        return null;
    }

    // The customer's other bookings: no overlapping time, and within the booking limits.
    private async Task<string?> ChangeConflictAsync(
        string userId, int reservationId, DateTime start, DateTime now)
    {
        var end = start.AddMinutes(BookingRules.DurationMinutes);

        var mine = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Id != reservationId &&
                        (r.Status == ReservationStatus.Pending ||
                         r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Seated) &&
                        r.ReservedAt > start.AddHours(-12) &&
                        r.ReservedAt < end.AddHours(12))
            .Select(r => new { r.ReservedAt, r.DurationMinutes })
            .ToListAsync();

        if (mine.Any(r => r.ReservedAt < end &&
                          r.ReservedAt.AddMinutes(r.DurationMinutes) > start))
            return "You already have another reservation at this time.";

        return await LimitMessageAsync(userId, start, now, reservationId);
    }

    // The party no longer fits the booked table type: list the other types that can take it at this time.
    private async Task<MyChangeCheck> NeedsOtherTypeAsync(
        DateTime start, int guests, int currentTypeId, string reason)
    {
        var result = await GetTypeOptionsAsync(start, guests);

        if (result.Error != null)
            return new MyChangeCheck(true, false, $"{reason} {result.Error}", new());

        var options = result.Options
            .Where(o => o.Status == TypeAvailability.Available && o.Id != currentTypeId)
            .Select(o => new MyChangeTypeOption(
                o.Id, o.Name, o.JapaneseName, o.MinGuests, o.MaxGuests, o.MinimumSpend, o.TablesLeft))
            .ToList();

        if (options.Count == 0)
            return new MyChangeCheck(true, false,
                $"{reason} No other table type is free for {(guests == 1 ? "1 guest" : guests + " guests")} at this time.", new());

        return new MyChangeCheck(true, false,
            $"{reason} Choose another table type for your party.", new(), true, options);
    }

    public async Task<MyChangeCheck> CheckMyChangeAsync(
        string userId, int reservationId, DateTime start, int guests, int? tableTypeId,
        CancellationToken cancellationToken = default)
    {
        await ReleaseOverdueReservationsAsync();

        var now = DateTime.Now;
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0);

        static MyChangeCheck Bad(string message, bool valid = true) => new(valid, false, message, new());

        var res = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Table)
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.UserId == userId, cancellationToken);

        if (res == null)
            return Bad("This reservation could not be found.", false);

        if (!CanChange(res, now))
            return Bad(res.ReservedAt <= now
                ? "You cannot modify a reservation after its booking time."
                : "This reservation can no longer be modified.", false);

        if (guests < 1 || guests > 20)
            return Bad("Choose between 1 and 20 guests.", false);

        var table = res.Table;
        if (table?.TableTypeDefinitionId is not int currentTypeId)
            return Bad("This reservation cannot be modified online. Please contact the restaurant.", false);

        var typeId = tableTypeId ?? currentTypeId;
        var timeChanged = start != res.ReservedAt;
        var typeChanged = typeId != currentTypeId;

        var type = await _db.TableTypeDefinitions
            .AsNoTracking()
            .Include(t => t.Tables)
            .FirstOrDefaultAsync(t => t.Id == typeId, cancellationToken);

        // 1. Only the number of guests changes: the booking keeps its table, time and status.
        if (!timeChanged && !typeChanged)
        {
            if (guests == res.Guests)
                return new MyChangeCheck(true, false, "This is your current booking.", new());

            var allowBelow = type?.AllowBelowMinGuests ?? false;

            if (guests <= table.Capacity && (guests >= table.MinGuests || allowBelow))
                return new MyChangeCheck(true, true, null, new(), false);

            var reason = guests > table.Capacity
                ? $"Your table seats up to {table.Capacity} guests."
                : $"Your table type needs at least {table.MinGuests} guests.";

            return await NeedsOtherTypeAsync(start, guests, currentTypeId, reason);
        }

        // 2. Date, time or table type changes: a table must be free and staff confirm again.
        var (hours, specials) = await LoadScheduleAsync();
        var slotError = ValidateSlot(start, hours, specials, now);

        if (slotError != null)
            return Bad(slotError, false);

        var problem = ChangeTypeProblem(type, guests);
        if (problem != null)
            return typeChanged
                ? Bad(problem)
                : await NeedsOtherTypeAsync(start, guests, currentTypeId, problem);

        if (timeChanged)
        {
            var conflict = await ChangeConflictAsync(userId, reservationId, start, now);
            if (conflict != null)
                return Bad(conflict);
        }

        // True when a table of the chosen type is free at that start time (this booking's own hold ignored).
        async Task<bool> HasTableAsync(DateTime when)
        {
            var busy = await BusyTableIdsAsync(
                when, when.AddMinutes(BookingRules.DurationMinutes), reservationId);

            return type!.Tables.Any(t => TableFits(t, type, guests) && !busy.Contains(t.Id));
        }

        if (await HasTableAsync(start))
            return new MyChangeCheck(true, true, null, new(), true);

        var timeText = start.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var message = $"No {type!.Name} table is free for {(guests == 1 ? "1 guest" : guests + " guests")} at {timeText}.";
        var suggestions = typeChanged
            ? new List<TimeSuggestion>()
            : await BuildSuggestionsAsync(HasTableAsync, start, hours, specials, now);

        return new MyChangeCheck(true, false, message, suggestions);
    }

    public async Task<MyReservationChangeResult> ChangeMyReservationAsync(
        string userId, int reservationId, DateTime start, int guests, int? tableTypeId,
        bool acceptedMinimumSpend, decimal expectedMinimumSpend, string? specialRequest = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || reservationId <= 0)
            return new(false, "not_found", "This reservation could not be found.");

        await ReleaseOverdueReservationsAsync();

        var now = DateTime.Now;
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0);
        var end = start.AddMinutes(BookingRules.DurationMinutes);

        if (guests < 1 || guests > 20)
            return new(false, "guests", "Choose between 1 and 20 guests.");

        // Same rules as a new booking: trimmed, no control characters, at most 500 characters.
        string? special = null;
        if (specialRequest != null)
        {
            special = new string(specialRequest.Trim().Where(c => !char.IsControl(c) || c == '\n').ToArray());

            if (special.Length > 500)
                return new(false, "special", "Special requests can be at most 500 characters.");
        }

        var (hours, specials) = await LoadScheduleAsync();

        // Serializable: two customers cannot both take the same table at the same moment.
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            var res = await _db.Reservations
                .Include(r => r.Table)
                .FirstOrDefaultAsync(r => r.Id == reservationId && r.UserId == userId, cancellationToken);

            if (res == null)
                return new(false, "not_found", "This reservation could not be found.");

            if (!CanChange(res, now))
            {
                if (res.ReservedAt <= now)
                    return new(false, "too_late", "You cannot modify a reservation after its booking time.");

                return new(false, "status_changed",
                    "This reservation can no longer be modified. Please refresh to see its latest status.");
            }

            if (res.Table?.TableTypeDefinitionId is not int currentTypeId)
                return new(false, "type", "This reservation cannot be modified online. Please contact the restaurant.");

            var typeId = tableTypeId ?? currentTypeId;
            var timeChanged = start != res.ReservedAt;
            var typeChanged = typeId != currentTypeId;

            var requestChanged = special != null && special != (res.SpecialRequest ?? "").Trim();

            if (!timeChanged && !typeChanged && guests == res.Guests && !requestChanged)
                return new(false, "unchanged", "Nothing has been modified. Pick a new date, time, number of guests or request.");

            var type = await _db.TableTypeDefinitions
                .Include(t => t.Tables)
                .FirstOrDefaultAsync(t => t.Id == typeId, cancellationToken);

            var from = res.Status;
            var oldAt = res.ReservedAt;
            var oldGuests = res.Guests;
            var current = res.Table!;

            // 1. Only the guests and/or the special request change: the booking keeps its table, time and status.
            if (!timeChanged && !typeChanged)
            {
                var notes = new List<string>();

                if (guests != res.Guests)
                {
                    var allowBelow = type?.AllowBelowMinGuests ?? false;

                    if (guests > current.Capacity || (guests < current.MinGuests && !allowBelow))
                        return new(false, "guests", guests > current.Capacity
                            ? $"Your table seats up to {current.Capacity} guests. Choose a larger table type for more."
                            : $"Your table type needs at least {current.MinGuests} guests.");

                    res.Guests = guests;
                    notes.Add($"Guests modified by customer: {oldGuests} -> {guests}");
                }

                if (requestChanged)
                {
                    res.SpecialRequest = special!.Length == 0 ? null : special;
                    notes.Add("Special request modified by customer");
                }

                _db.ReservationStatusLogs.Add(new ReservationStatusLog
                {
                    ReservationId = res.Id,
                    FromStatus = from,
                    ToStatus = from,
                    ChangedByUserId = userId,
                    ChangedByRole = "Customer",
                    ChangedAt = now,
                    Note = string.Join("; ", notes)
                });

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return new(true, "changed", "Your reservation has been modified.",
                    res.ReferenceNo, res.ReservedAt, guests, false, requestChanged);
            }

            // 2. Date, time or table type changes.
            var slotError = ValidateSlot(start, hours, specials, now);
            if (slotError != null)
                return new(false, "slot", slotError);

            var typeProblem = ChangeTypeProblem(type, guests);
            if (typeProblem != null)
                return new(false, "type", typeProblem);

            if (timeChanged)
            {
                var conflict = await ChangeConflictAsync(userId, reservationId, start, now);
                if (conflict != null)
                    return new(false,
                        conflict.StartsWith("You already have", StringComparison.Ordinal) ? "overlap" : "limit",
                        conflict);
            }

            // Ignore this booking's own hold on its table, so moving 30 minutes does not clash with itself.
            var busy = await BusyTableIdsAsync(start, end, reservationId);

            var table = !typeChanged && TableFits(current, type!, guests) && !busy.Contains(current.Id)
                ? current
                : type!.Tables
                    .Where(t => TableFits(t, type, guests) && !busy.Contains(t.Id))
                    .OrderBy(t => t.Capacity)
                    .ThenBy(t => t.TableNumber)
                    .FirstOrDefault();

            if (table == null)
                return new(false, "full",
                    $"No {type.Name} table is free at that time. Please choose another time.");

            // A new table type may come with a minimum spend the customer has not accepted yet.
            if (typeChanged)
            {
                var spend = table.MinimumSpend;

                if (expectedMinimumSpend != spend)
                    return new(false, "spend",
                        "The minimum spend has been updated. Please review it and accept it again.");

                if (spend > 0m && !acceptedMinimumSpend)
                    return new(false, "spend", "Please accept the minimum spend to continue.");

                res.MinimumSpendAccepted = spend;
                res.MinimumSpendAcceptedAt = spend > 0m ? now : null;
            }

            res.ReservedAt = start;
            res.Guests = guests;
            res.TableId = table.Id;
            res.Status = ReservationStatus.Pending;

            if (requestChanged)
                res.SpecialRequest = special!.Length == 0 ? null : special;

            _db.ReservationStatusLogs.Add(new ReservationStatusLog
            {
                ReservationId = res.Id,
                FromStatus = from,
                ToStatus = ReservationStatus.Pending,
                ChangedByUserId = userId,
                ChangedByRole = "Customer",
                ChangedAt = now,
                Note = "Modified by customer: " +
                       $"{oldAt.ToString("d MMM h:mm tt", CultureInfo.InvariantCulture)}, {oldGuests} guests -> " +
                       $"{start.ToString("d MMM h:mm tt", CultureInfo.InvariantCulture)}, {guests} guests" +
                       (typeChanged ? $", table type {type.Name}" : "") +
                       (requestChanged ? ", special request modified" : "")
            });

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new(true, "changed", "Your reservation has been modified.",
                res.ReferenceNo, start, guests, true, requestChanged);
        }
        catch (Exception ex) when (
            ex is DbUpdateException ||
            ex.GetBaseException() is Microsoft.Data.SqlClient.SqlException)
        {
            // Most often a deadlock: someone else took the table first.
            return new(false, "busy", "Someone just booked that table. Please try again.");
        }
    }
}
