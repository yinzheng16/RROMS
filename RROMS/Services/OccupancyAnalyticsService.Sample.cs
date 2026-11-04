// Owner: Member 2 (Tables & Reservations) - US20. Sample data for demos (Development only; the page hides the buttons elsewhere).
//
// Sample reservations have a reference starting with "DEMO-".
// Sample walk-in sessions have no reservation and an opening time ending in .777 seconds, so they can be removed
// again without ever touching real walk-ins.
using Microsoft.EntityFrameworkCore;
using RROMS.Models;

namespace RROMS.Services;

public partial class OccupancyAnalyticsService
{
    private const string SamplePrefix = "DEMO-";
    private const int SampleDays = 90;

    public async Task<int> LoadSampleAsync(string userId, CancellationToken ct = default)
    {
        await ClearSampleAsync(ct);

        var tables = await _db.RestaurantTables.AsNoTracking()
            .Where(t => t.IsActive && t.DiningArea.IsActive)
            .Select(t => new { t.Id, t.Capacity }).ToListAsync(ct);
        if (tables.Count == 0) return 0;

        var hours = await _db.OperatingHours.AsNoTracking().ToListAsync(ct);
        var specials = await _db.SpecialDates.AsNoTracking().ToListAsync(ct);

        var rng = new Random(2026);
        var now = DateTime.Now;
        var today = DateTime.Today;
        var busy = tables.ToDictionary(t => t.Id, _ => new List<Seg>());
        var weight = new[] { 0.30, 0.35, 0.40, 0.50, 0.85, 1.00, 0.90 };      // Mon..Sun
        var added = 0;
        var counter = 0;

        bool Free(int tableId, DateTime s, DateTime e) =>
            busy[tableId].All(b => e.AddMinutes(BookingRules.TurnoverMinutes) <= b.S || s >= b.E.AddMinutes(BookingRules.TurnoverMinutes));

        // a start time near lunch or dinner that fits inside the opening periods
        DateTime? PickStart(DateTime day, List<Seg> segs, int minutes)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var dinner = rng.NextDouble() < 0.65;
                var centre = dinner ? 19.25 : 12.75;
                var spread = dinner ? 1.25 : 0.9;
                var u1 = 1.0 - rng.NextDouble();
                var u2 = rng.NextDouble();
                var gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                var h = Math.Round((centre + gauss * spread) * 2) / 2.0;          // every 30 minutes
                var start = day.AddHours(h);
                var end = start.AddMinutes(minutes);
                if (segs.Any(sg => start >= sg.S && end <= sg.E)) return start;
            }
            return null;
        }

        for (var i = 0; i <= SampleDays - 1; i++)
        {
            var day = today.AddDays(-(SampleDays - 1) + i);
            var segs = Segments(day, hours, specials);
            if (segs.Count == 0) continue;

            var growth = 0.8 + 0.3 * i / (SampleDays - 1.0);                     // the restaurant slowly gets busier
            var n = (int)Math.Round(tables.Count * 1.2 * weight[WeekdayIndex(day.DayOfWeek)] * growth * (0.8 + rng.NextDouble() * 0.4));

            for (var k = 0; k < n; k++)
            {
                var guests = Pick(rng, new[] { 2, 4, 3, 1, 6, 5 }, new[] { 40, 30, 10, 8, 8, 4 });
                var start = PickStart(day, segs, BookingRules.DurationMinutes);
                if (start == null) continue;
                var end = start.Value.AddMinutes(BookingRules.DurationMinutes);

                var table = tables.Where(t => t.Capacity >= guests && Free(t.Id, start.Value, end))
                                  .OrderBy(t => t.Capacity).ThenBy(_ => rng.Next()).FirstOrDefault();
                if (table == null) continue;

                var roll = rng.NextDouble();
                ReservationStatus status;
                if (start.Value > now) status = roll < 0.8 ? ReservationStatus.Confirmed : ReservationStatus.Pending;
                else if (end > now) status = ReservationStatus.Seated;
                else if (roll < 0.08) status = ReservationStatus.Cancelled;
                else if (roll < 0.14) status = ReservationStatus.NoShow;
                else status = ReservationStatus.Completed;

                if (status != ReservationStatus.Cancelled) busy[table.Id].Add(new Seg(start.Value, end));

                var res = new Reservation
                {
                    ReferenceNo = SamplePrefix + (++counter).ToString("D5"),
                    UserId = userId,
                    TableId = table.Id,
                    ReservedAt = start.Value,
                    DurationMinutes = BookingRules.DurationMinutes,
                    Guests = guests,
                    Status = status,
                    CreatedAt = start.Value.AddDays(-rng.Next(1, 6))
                };
                _db.Reservations.Add(res);
                added++;

                if (status == ReservationStatus.Completed || status == ReservationStatus.Seated)
                {
                    var opened = start.Value.AddMinutes(rng.Next(-3, 13));
                    var stay = rng.Next(55, 111);
                    _db.DiningSessions.Add(new DiningSession
                    {
                        TableId = table.Id,
                        Reservation = res,
                        OpenedAt = opened,
                        ClosedAt = status == ReservationStatus.Completed ? opened.AddMinutes(stay) : null,
                        Status = status == ReservationStatus.Completed ? DiningSessionStatus.Closed : DiningSessionStatus.Open
                    });
                }
            }

            // a few walk-ins with no reservation
            var walkIns = rng.Next(0, tables.Count / 4 + 2);
            for (var w = 0; w < walkIns; w++)
            {
                var stay = rng.Next(45, 101);
                var start = PickStart(day, segs, stay);
                if (start == null) continue;
                var end = start.Value.AddMinutes(stay);
                if (end > now) continue;
                var party = rng.Next(1, 5);
                var table = tables.Where(t => t.Capacity >= party && Free(t.Id, start.Value, end))
                                  .OrderBy(t => t.Capacity).ThenBy(_ => rng.Next()).FirstOrDefault();
                if (table == null) continue;

                busy[table.Id].Add(new Seg(start.Value, end));
                _db.DiningSessions.Add(new DiningSession
                {
                    TableId = table.Id,
                    OpenedAt = start.Value.AddMilliseconds(777),                 // the sample marker
                    ClosedAt = end,
                    Status = DiningSessionStatus.Closed
                });
                added++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return added;
    }

    public async Task<int> ClearSampleAsync(CancellationToken ct = default)
    {
        var sessions = await _db.DiningSessions
            .Where(s => (s.Reservation != null && s.Reservation.ReferenceNo.StartsWith(SamplePrefix))
                        || (s.ReservationId == null && s.OpenedAt.Millisecond == 777))
            .ToListAsync(ct);
        var reservations = await _db.Reservations
            .Where(r => r.ReferenceNo.StartsWith(SamplePrefix)).ToListAsync(ct);

        _db.DiningSessions.RemoveRange(sessions);
        _db.Reservations.RemoveRange(reservations);
        await _db.SaveChangesAsync(ct);
        return sessions.Count + reservations.Count;
    }

    // picks one value using whole-number weights
    private static int Pick(Random rng, int[] values, int[] weights)
    {
        var roll = rng.Next(weights.Sum());
        for (var i = 0; i < values.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0) return values[i];
        }
        return values[0];
    }
}
