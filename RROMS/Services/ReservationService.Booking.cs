// Owner: Member 2 (Tables & Reservations) - US12 make a reservation, US14 availability checks.
// This is a second part of ReservationService (the class is marked "partial").
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;

namespace RROMS.Services;

public partial class ReservationService
{
    // One opening session of the restaurant. A session that closes after midnight
    // still belongs to the day it opened on.
    private sealed record BookingSession(
        DateTime Open,
        DateTime Close,
        DateTime? BreakStart,
        DateTime? BreakEnd,
        string? ClosedReason);

    // ---------- opening hours, special dates and breaks ----------

    private async Task<(List<OperatingHour> Hours, List<SpecialDate> Specials)> LoadScheduleAsync()
    {
        var hours = await GetOperatingHoursAsync();
        var specials = await _db.SpecialDates.AsNoTracking().ToListAsync();
        return (hours, specials);
    }

    private static BookingSession SessionFor(
        DateTime date, List<OperatingHour> hours, List<SpecialDate> specials)
    {
        date = date.Date;

        // A special date (or date range) replaces the normal weekly hours.
        var special = specials.FirstOrDefault(s =>
            s.Date.Date <= date && (s.EndDate ?? s.Date).Date >= date);

        if (special != null)
        {
            if (special.IsClosed || !special.OpenTime.HasValue || !special.CloseTime.HasValue)
                return new BookingSession(date, date, null, null, $"We are closed for {special.Name}.");

            var sOpen = date + special.OpenTime.Value;
            var sClose = date + special.CloseTime.Value;
            if (sClose <= sOpen) sClose = sClose.AddDays(1);

            return new BookingSession(sOpen, sClose, null, null, null);
        }

        var h = hours.FirstOrDefault(x => x.Day == date.DayOfWeek);

        if (h == null || h.IsClosed)
            return new BookingSession(date, date, null, null, "We are closed on this day.");

        var open = date + h.OpenTime.ToTimeSpan();
        var close = date + h.CloseTime.ToTimeSpan();
        if (close <= open) close = close.AddDays(1);

        DateTime? breakStart = null;
        DateTime? breakEnd = null;

        if (h.BreakStart.HasValue && h.BreakEnd.HasValue)
        {
            var bs = date + h.BreakStart.Value.ToTimeSpan();
            if (bs < open) bs = bs.AddDays(1);

            var be = date + h.BreakEnd.Value.ToTimeSpan();
            if (be <= bs) be = be.AddDays(1);

            breakStart = bs;
            breakEnd = be;
        }

        return new BookingSession(open, close, breakStart, breakEnd, null);
    }

    private static bool OverlapsBreak(BookingSession s, DateTime start, DateTime end) =>
        s.BreakStart.HasValue && s.BreakEnd.HasValue &&
        start < s.BreakEnd.Value && end > s.BreakStart.Value;

    // All start times a customer may pick on one session.
    private static List<DateTime> SlotTimes(BookingSession s, DateTime now)
    {
        var list = new List<DateTime>();
        if (s.ClosedReason != null) return list;

        var earliest = now.AddMinutes(BookingRules.MinLeadMinutes);

        for (var t = s.Open;
             t.AddMinutes(BookingRules.DurationMinutes) <= s.Close;
             t = t.AddMinutes(BookingRules.SlotMinutes))
        {
            if (t < earliest) continue;
            if (OverlapsBreak(s, t, t.AddMinutes(BookingRules.DurationMinutes))) continue;
            list.Add(t);
        }

        return list;
    }

    // Returns an error message, or null when the start time is a valid booking time.
    private static string? ValidateSlot(
        DateTime start, List<OperatingHour> hours, List<SpecialDate> specials, DateTime now)
    {
        if (start < now)
            return "That time has already passed.";

        if (start < now.AddMinutes(BookingRules.MinLeadMinutes))
            return "Please book at least 1 hour ahead.";

        if (start.Date > now.Date.AddDays(BookingRules.MaxAdvanceDays))
            return $"Reservations open up to {BookingRules.MaxAdvanceDays} days ahead.";

        var end = start.AddMinutes(BookingRules.DurationMinutes);

        // The booking belongs to today's session, or to yesterday's if that one runs past midnight.
        foreach (var day in new[] { start.Date, start.Date.AddDays(-1) })
        {
            var s = SessionFor(day, hours, specials);
            if (s.ClosedReason != null) continue;
            if (start < s.Open || end > s.Close) continue;

            var offset = (start - s.Open).TotalMinutes;
            if (start.Second != 0 || offset % BookingRules.SlotMinutes != 0)
                return "Please choose one of the listed times.";

            if (OverlapsBreak(s, start, end))
                return "That time falls in our break. Please choose another time.";

            return null;
        }

        var sameDay = SessionFor(start.Date, hours, specials);
        if (sameDay.ClosedReason != null) return sameDay.ClosedReason;

        return "That time is outside our opening hours, or too close to closing for a full sitting.";
    }

    // ---------- step 1: dates and times ----------

    public async Task<List<string>> GetBookableDatesAsync()
    {
        var (hours, specials) = await LoadScheduleAsync();
        var now = DateTime.Now;
        var dates = new List<string>();

        for (var i = 0; i <= BookingRules.MaxAdvanceDays; i++)
        {
            var day = now.Date.AddDays(i);

            if (SlotTimes(SessionFor(day, hours, specials), now).Count > 0)
                dates.Add(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return dates;
    }

    public async Task<List<BookingSlot>> GetTimeSlotsAsync(DateTime date)
    {
        var (hours, specials) = await LoadScheduleAsync();
        var now = DateTime.Now;

        if (date.Date > now.Date.AddDays(BookingRules.MaxAdvanceDays))
            return new List<BookingSlot>();

        return SlotTimes(SessionFor(date, hours, specials), now)
            .Select(t => new BookingSlot(
                t.ToString("HH:mm", CultureInfo.InvariantCulture),
                t.ToString("h:mm tt", CultureInfo.InvariantCulture)))
            .ToList();
    }

    // Largest party any active table type can seat (the guest stepper stops here).
    public async Task<int> GetMaxPartySizeAsync()
    {
        var max = await _db.TableTypeDefinitions
            .AsNoTracking()
            .Where(t => t.IsActive && t.Tables.Any())
            .Select(t => (int?)t.DefaultMaxGuests)
            .MaxAsync();

        return Math.Clamp(max ?? 2, 1, 20);
    }

    // ---------- availability ----------

    // Table ids that already have a booking touching [start, end], plus a turnover gap.
    private async Task<HashSet<int>> BusyTableIdsAsync(DateTime start, DateTime end)
    {
        var from = start.AddHours(-12);
        var to = end.AddHours(12);

        var rows = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.TableId != null &&
                        (r.Status == ReservationStatus.Pending ||
                         r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Seated) &&
                        r.ReservedAt > from &&
                        r.ReservedAt < to)
            .Select(r => new { r.TableId, r.ReservedAt, r.DurationMinutes })
            .ToListAsync();

        var gap = TimeSpan.FromMinutes(BookingRules.TurnoverMinutes);

        return rows
            .Where(r => r.ReservedAt < end + gap &&
                        r.ReservedAt.AddMinutes(r.DurationMinutes) + gap > start)
            .Select(r => r.TableId!.Value)
            .ToHashSet();
    }

    private static bool TableFits(RestaurantTable table, TableTypeDefinition type, int guests) =>
        table.IsActive &&
        table.Status != TableStatus.OutOfService &&
        guests <= table.Capacity &&
        (guests >= table.MinGuests || type.AllowBelowMinGuests);

    // ---------- step 2: table types ----------

    public async Task<TypeOptionsResult> GetTypeOptionsAsync(DateTime start, int guests)
    {
        await ReleaseOverdueReservationsAsync();

        if (guests < 1 || guests > 20)
            return new TypeOptionsResult("Choose between 1 and 20 guests.", new());

        var (hours, specials) = await LoadScheduleAsync();
        var error = ValidateSlot(start, hours, specials, DateTime.Now);

        if (error != null)
            return new TypeOptionsResult(error, new());

        var end = start.AddMinutes(BookingRules.DurationMinutes);
        var busy = await BusyTableIdsAsync(start, end);

        // Inactive types are hidden: customers cannot do anything with them.
        var types = await _db.TableTypeDefinitions
            .AsNoTracking()
            .Include(t => t.Tables)
            .Where(t => t.IsActive)
            .ToListAsync();

        var timeText = start.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var options = new List<TypeOption>();

        foreach (var type in types)
        {
            if (type.Tables.Count == 0) continue;

            var spend = type.DefaultMinimumSpend ?? 0m;
            var below = guests < type.DefaultMinGuests;
            var status = TypeAvailability.Available;
            string? reason = null;
            var left = 0;

            if (guests > type.DefaultMaxGuests)
            {
                status = TypeAvailability.NotSuitable;
                reason = type.DefaultMaxGuests == 1
                    ? $"Too small for {guests} guests."
                    : $"Seats up to {type.DefaultMaxGuests} guests.";
            }
            else if (below && !type.AllowBelowMinGuests)
            {
                status = TypeAvailability.NotSuitable;
                reason = $"Minimum {type.DefaultMinGuests} guests.";
            }
            else if (!type.Tables.Any(t => t.IsActive && t.Status != TableStatus.OutOfService))
            {
                status = TypeAvailability.Unavailable;
                reason = "Temporarily unavailable.";
            }
            else
            {
                left = type.Tables.Count(t => TableFits(t, type, guests) && !busy.Contains(t.Id));

                if (left == 0)
                {
                    status = TypeAvailability.FullyBooked;
                    reason = $"No tables left at {timeText}.";
                }
            }

            options.Add(new TypeOption(
                type.Id,
                type.Name,
                type.JapaneseName,
                type.Description,
                type.PhotoPath,
                type.DefaultMinGuests,
                type.DefaultMaxGuests,
                spend,
                below && type.AllowBelowMinGuests,
                status,
                reason,
                left));
        }

        return new TypeOptionsResult(
            null,
            options
                .OrderBy(o => o.Status)
                .ThenBy(o => o.MinGuests)
                .ThenBy(o => o.Name)
                .ToList());
    }

    // ---------- step 3: save the reservation ----------

    public async Task<BookingResult> CreateReservationAsync(CreateReservationRequest req)
    {
        await ReleaseOverdueReservationsAsync();

        var now = DateTime.Now;
        var start = new DateTime(
            req.ReservedAt.Year, req.ReservedAt.Month, req.ReservedAt.Day,
            req.ReservedAt.Hour, req.ReservedAt.Minute, 0);
        var end = start.AddMinutes(BookingRules.DurationMinutes);

        if (req.Guests < 1 || req.Guests > 20)
            return BookingResult.Fail("guests", "Choose between 1 and 20 guests.");

        var special = (req.SpecialRequest ?? "").Trim();
        special = new string(special.Where(c => !char.IsControl(c) || c == '\n').ToArray());

        if (special.Length > 500)
            return BookingResult.Fail("special", "Special requests can be at most 500 characters.");

        var token = string.IsNullOrWhiteSpace(req.Token) ? null : req.Token.Trim();
        if (token != null && token.Length > 40)
            return BookingResult.Fail("token", "Something went wrong. Please try again.");

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == req.UserId);

        if (user == null || !user.IsActive)
            return BookingResult.Fail("user", "Your account cannot make reservations.");

        var (hours, specials) = await LoadScheduleAsync();
        var slotError = ValidateSlot(start, hours, specials, now);

        if (slotError != null)
            return BookingResult.Fail("slot", slotError);

        // Serializable: two customers sending the same table at the same moment cannot both pass.
        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            // Same click sent twice (double click, refresh): return the first result.
            if (token != null)
            {
                var again = await _db.Reservations
                    .AsNoTracking()
                    .Where(r => r.UserId == req.UserId && r.SubmissionToken == token)
                    .Select(r => r.Id)
                    .FirstOrDefaultAsync();

                if (again != 0)
                {
                    await tx.RollbackAsync();
                    var ticket = await BuildTicketAsync(again);
                    return ticket != null
                        ? BookingResult.Ok(ticket)
                        : BookingResult.Fail("token", "Something went wrong. Please try again.");
                }
            }

            // The customer may not hold two overlapping bookings.
            var mine = await _db.Reservations
                .AsNoTracking()
                .Where(r => r.UserId == req.UserId &&
                            (r.Status == ReservationStatus.Pending ||
                             r.Status == ReservationStatus.Confirmed ||
                             r.Status == ReservationStatus.Seated) &&
                            r.ReservedAt > start.AddHours(-12) &&
                            r.ReservedAt < end.AddHours(12))
                .Select(r => new { r.ReservedAt, r.DurationMinutes })
                .ToListAsync();

            if (mine.Any(r => r.ReservedAt < end &&
                              r.ReservedAt.AddMinutes(r.DurationMinutes) > start))
            {
                await tx.RollbackAsync();
                return BookingResult.Fail("overlap",
                    "You already have a reservation at this time.");
            }

            var upcoming = await _db.Reservations.CountAsync(r =>
                r.UserId == req.UserId &&
                (r.Status == ReservationStatus.Pending ||
                 r.Status == ReservationStatus.Confirmed) &&
                r.ReservedAt > now);

            if (upcoming >= BookingRules.MaxActiveBookings)
            {
                await tx.RollbackAsync();
                return BookingResult.Fail("limit",
                    $"You can have up to {BookingRules.MaxActiveBookings} upcoming reservations. " +
                    "Cancel one before booking another.");
            }

            var type = await _db.TableTypeDefinitions
                .Include(t => t.Tables)
                .Include(t => t.DefaultDiningArea)
                .FirstOrDefaultAsync(t => t.Id == req.TableTypeId);

            if (type == null || !type.IsActive)
            {
                await tx.RollbackAsync();
                return BookingResult.Fail("type", "This table type is no longer available.");
            }

            if (req.Guests > type.DefaultMaxGuests ||
                (req.Guests < type.DefaultMinGuests && !type.AllowBelowMinGuests))
            {
                await tx.RollbackAsync();
                return BookingResult.Fail("type",
                    "This table type does not suit your party size. Please choose another.");
            }

            var busy = await BusyTableIdsAsync(start, end);

            var table = type.Tables
                .Where(t => TableFits(t, type, req.Guests) && !busy.Contains(t.Id))
                .OrderBy(t => t.Capacity)
                .ThenBy(t => t.TableNumber)
                .FirstOrDefault();

            if (table == null)
            {
                await tx.RollbackAsync();
                return BookingResult.Fail("full",
                    "That table type has just been fully booked. Please choose another.");
            }

            var spend = table.MinimumSpend;

            if (req.ExpectedMinimumSpend != spend)
            {
                await tx.RollbackAsync();
                return BookingResult.Fail("spend",
                    "The minimum spend has changed. Please review it and accept it again.");
            }

            if (spend > 0m && !req.AcceptedMinimumSpend)
            {
                await tx.RollbackAsync();
                return BookingResult.Fail("spend",
                    "Please accept the minimum spend to continue.");
            }

            var reservation = new Reservation
            {
                ReferenceNo = await NewReferenceAsync(),
                UserId = req.UserId,
                TableId = table.Id,
                ReservedAt = start,
                DurationMinutes = BookingRules.DurationMinutes,
                Guests = req.Guests,
                SpecialRequest = special.Length == 0 ? null : special,
                Status = ReservationStatus.Pending,
                CreatedAt = now,
                MinimumSpendAccepted = spend,
                MinimumSpendAcceptedAt = spend > 0m ? now : null,
                SubmissionToken = token
            };

            _db.Reservations.Add(reservation);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return BookingResult.Ok(new ReservationTicket(
                reservation.ReferenceNo,
                reservation.ReservedAt,
                reservation.Guests,
                type.Name,
                type.JapaneseName,
                type.DefaultDiningArea?.Name,
                reservation.SpecialRequest,
                spend,
                reservation.Status.ToString()));
        }
        catch (Exception ex) when (
            ex is DbUpdateException ||
            ex.GetBaseException() is Microsoft.Data.SqlClient.SqlException)
        {
            // Most often a deadlock: another customer took the same table first.
            return BookingResult.Fail("busy",
                "Someone just booked that table. Please try again.");
        }
    }

    private async Task<ReservationTicket?> BuildTicketAsync(int reservationId)
    {
        var r = await _db.Reservations
            .AsNoTracking()
            .Include(x => x.Table)
                .ThenInclude(t => t!.TableTypeDefinition)
                    .ThenInclude(td => td!.DefaultDiningArea)
            .FirstOrDefaultAsync(x => x.Id == reservationId);

        if (r == null) return null;

        var type = r.Table?.TableTypeDefinition;

        return new ReservationTicket(
            r.ReferenceNo,
            r.ReservedAt,
            r.Guests,
            type?.Name ?? "Table",
            type?.JapaneseName,
            type?.DefaultDiningArea?.Name,
            r.SpecialRequest,
            r.MinimumSpendAccepted ?? 0m,
            r.Status.ToString());
    }

    // e.g. YR-7K3M9Q (no 0/O/1/I, easy to read out over the phone)
    private async Task<string> NewReferenceAsync()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var chars = new char[6];

            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

            var reference = "YR-" + new string(chars);

            if (!await _db.Reservations.AnyAsync(r => r.ReferenceNo == reference))
                return reference;
        }

        // Practically unreachable; keeps the value unique and within 20 characters.
        return "YR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }

    // ---------- no-show release ----------

    // A Pending or Confirmed booking that nobody has seated 15 minutes after its time
    // becomes No-show, which frees the table. Seated bookings are never touched.
    public async Task<int> ReleaseOverdueReservationsAsync()
    {
        var cutoff = DateTime.Now.AddMinutes(-BookingRules.NoShowGraceMinutes);

        var overdue = await _db.Reservations
            .Where(r => (r.Status == ReservationStatus.Pending ||
                         r.Status == ReservationStatus.Confirmed) &&
                        r.ReservedAt < cutoff)
            .ToListAsync();

        if (overdue.Count == 0) return 0;

        foreach (var r in overdue)
            r.Status = ReservationStatus.NoShow;

        await _db.SaveChangesAsync();
        return overdue.Count;
    }
}
