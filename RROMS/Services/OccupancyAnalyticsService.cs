// Owner: Member 2 (Tables & Reservations) - US20 table occupancy and reservation analytics.
//
// How "busy" is worked out
//  * A table is occupied while a dining session is open, or (when no session exists) during a Confirmed / Seated /
//    Completed reservation. Walk-ins are dining sessions that have no reservation.
//  * Only opening hours count: closed days, special dates and the afternoon break are left out of both sides.
//  * Heatmap box = occupied table-minutes / open table-minutes for that weekday and hour, over the whole date range.
//  * Table % = occupied minutes / opening minutes of that one table.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public partial class OccupancyAnalyticsService : IOccupancyAnalyticsService
{
    private readonly ApplicationDbContext _db;
    public OccupancyAnalyticsService(ApplicationDbContext db) => _db = db;

    // ---------- small data holders ----------

    private sealed record TableInfo(int Id, string Name, string TypeName, string AreaName, int Capacity);
    private sealed record ResRow(int Id, int? TableId, DateTime ReservedAt, int Duration, int Guests, ReservationStatus Status);
    private sealed record SessRow(int Id, int TableId, int? ReservationId, DateTime OpenedAt, DateTime? ClosedAt);
    private sealed record Seg(DateTime S, DateTime E);

    // cut opening hours off at "now": today counts only up to the current time and the future not at all,
    // so a half-finished day does not drag the utilisation down and booked-but-not-yet-happened tables are not counted
    private static List<Seg> UpTo(List<Seg> segs, DateTime now) =>
        segs.Where(sg => sg.S < now).Select(sg => sg.E > now ? new Seg(sg.S, now) : sg).ToList();
    private sealed record Interval(int TableId, DateTime Start, DateTime End, bool WalkIn, int Guests);

    private sealed class DataSet
    {
        public OccupancyFilter Filter = null!;
        public DateTime Start, End, Now;                       // End is the day after Filter.To
        public List<TableInfo> Tables = new();
        public List<ResRow> InRange = new();                   // reservations whose booking time is inside the range
        public List<SessRow> Sessions = new();
        public List<Interval> Intervals = new();
        public List<OperatingHour> Hours = new();
        public List<SpecialDate> Specials = new();
    }

    private sealed class Analysis
    {
        public double[,] Avail = new double[7, 24];
        public double[,] Occ = new double[7, 24];
        public int[,] Cnt = new int[7, 24];
        public Dictionary<DateTime, double> DayOcc = new();
        public Dictionary<DateTime, double> DayAvail = new();
        public Dictionary<int, double> TableOcc = new();
        public Dictionary<int, int> TableBookings = new();
        public Dictionary<int, int> TableWalkIns = new();
        public double OpenMinutesPerTable;
    }

    // ---------- public API ----------

    public async Task<OccupancyChoices> ChoicesAsync(CancellationToken ct = default)
    {
        var areas = await _db.DiningAreas.AsNoTracking().Where(a => a.IsActive)
            .OrderBy(a => a.Id).Select(a => new OccupancyChoice(a.Id, a.Name)).ToListAsync(ct);
        var types = await _db.TableTypeDefinitions.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.Id).Select(t => new OccupancyChoice(t.Id, t.Name)).ToListAsync(ct);

        // which table types really sit in which area (looked at from the tables, not from a type's "default area")
        var pairs = await _db.RestaurantTables.AsNoTracking()
            .Where(t => t.IsActive && t.DiningArea.IsActive && t.TableTypeDefinition != null && t.TableTypeDefinition.IsActive)
            .Select(t => new { t.DiningAreaId, TypeId = t.TableTypeDefinitionId!.Value })
            .Distinct().ToListAsync(ct);
        var map = pairs.GroupBy(x => x.DiningAreaId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.TypeId).OrderBy(i => i).ToList());

        return new OccupancyChoices(areas, types, map);
    }

    public async Task<OccupancyReport> BuildAsync(OccupancyFilter filter, CancellationToken ct = default)
    {
        var ds = await LoadAsync(filter, ct);
        var an = Analyse(ds);
        var totals = BuildTotals(ds, an);

        // the same number of days just before the range, for the "compared with" arrows
        var length = (int)(ds.End - ds.Start).TotalDays;
        var prevTo = ds.Start.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(length - 1));
        var prevDs = await LoadAsync(filter with { From = prevFrom, To = prevTo }, ct);
        var prevTotals = BuildTotals(prevDs, Analyse(prevDs));
        var hasPrev = prevTotals.Reservations > 0 || prevTotals.WalkIns > 0;

        var heat = BuildHeatmap(an);
        var hasSample = await _db.Reservations.AsNoTracking().AnyAsync(r => r.ReferenceNo.StartsWith("DEMO-"), ct);

        return new OccupancyReport(
            filter, totals,
            hasPrev ? prevTotals : null,
            hasPrev ? prevFrom : null,
            hasPrev ? prevTo : null,
            heat, BuildWeeks(ds, an), BuildTableRows(ds, an),
            FindBusiest(heat), FindQuietest(heat), hasSample);
    }

    public async Task<OccupancySlotDetail> SlotAsync(OccupancyFilter filter, int day, int hour, CancellationToken ct = default)
    {
        var ds = await LoadAsync(filter, ct);
        var names = ds.Tables.ToDictionary(t => t.Id);

        double occMin = 0, availMin = 0, guestSum = 0;
        int reservations = 0, walkIns = 0, daysCounted = 0;
        var tableMin = new Dictionary<int, double>();
        var perDateRes = new Dictionary<DateTime, int>();
        var perDateWalk = new Dictionary<DateTime, int>();

        for (var d = ds.Start; d < ds.End; d = d.AddDays(1))
        {
            if (WeekdayIndex(d.DayOfWeek) != day) continue;
            var ws = d.AddHours(hour);
            var we = ws.AddHours(1);
            var open = UpTo(Segments(d, ds.Hours, ds.Specials), ds.Now)
                .Select(sg => new Seg(Max(sg.S, ws), Min(sg.E, we)))
                .Where(x => x.E > x.S).ToList();
            if (open.Count == 0) continue;

            daysCounted++;
            availMin += open.Sum(x => (x.E - x.S).TotalMinutes) * ds.Tables.Count;

            foreach (var iv in ds.Intervals)
            {
                double mins = 0;
                foreach (var o in open)
                {
                    var s = Max(iv.Start, o.S);
                    var e = Min(iv.End, o.E);
                    if (e > s) mins += (e - s).TotalMinutes;
                }
                if (mins <= 0) continue;

                occMin += mins;
                tableMin[iv.TableId] = tableMin.GetValueOrDefault(iv.TableId) + mins;
                if (iv.WalkIn) { walkIns++; perDateWalk[d] = perDateWalk.GetValueOrDefault(d) + 1; }
                else { reservations++; guestSum += iv.Guests; perDateRes[d] = perDateRes.GetValueOrDefault(d) + 1; }
            }
        }

        var top = tableMin.OrderByDescending(kv => kv.Value).Take(3)
            .Where(kv => names.ContainsKey(kv.Key))
            .Select(kv => new OccupancySlotTable(names[kv.Key].Name, names[kv.Key].TypeName, Math.Round(kv.Value / 60.0, 1)))
            .ToList();

        // the busiest single day for this slot (most parties; the later day wins a tie)
        DateTime? bestDate = null;
        var bestTotal = 0;
        foreach (var day0 in perDateRes.Keys.Union(perDateWalk.Keys).OrderBy(k => k))
        {
            var total = perDateRes.GetValueOrDefault(day0) + perDateWalk.GetValueOrDefault(day0);
            if (total >= bestTotal) { bestDate = day0; bestTotal = total; }
        }
        var bestRes = bestDate.HasValue ? perDateRes.GetValueOrDefault(bestDate.Value) : 0;
        var bestWalk = bestDate.HasValue ? perDateWalk.GetValueOrDefault(bestDate.Value) : 0;

        double? percent = availMin > 0 ? Math.Round(Math.Min(100, occMin / availMin * 100), 1) : null;
        var avgGuests = reservations > 0 ? Math.Round(guestSum / reservations, 1) : 0;

        return new OccupancySlotDetail(day, hour, percent, reservations, walkIns, avgGuests, top,
            bestDate, bestRes, bestWalk, daysCounted);
    }

    public async Task<string> ExportCsvAsync(OccupancyFilter filter, CancellationToken ct = default)
    {
        var ds = await LoadAsync(filter, ct);
        var an = Analyse(ds);
        var totals = BuildTotals(ds, an);
        var heat = BuildHeatmap(an);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("Table occupancy report");
        sb.AppendLine($"From,{filter.From:yyyy-MM-dd}");
        sb.AppendLine($"To,{filter.To:yyyy-MM-dd}");
        sb.AppendLine($"Counted,{(filter.IncludeWalkIns ? "Reservations and walk-ins" : "Reservations only")}");
        sb.AppendLine($"Tables,{totals.TableCount}");
        sb.AppendLine($"Reservations,{totals.Reservations}");
        sb.AppendLine($"Completed,{totals.Completed}");
        sb.AppendLine($"Cancelled,{totals.Cancelled}");
        sb.AppendLine($"No-show,{totals.NoShow}");
        sb.AppendLine($"Walk-ins,{totals.WalkIns}");
        sb.AppendLine($"Average utilisation %,{totals.AvgUtilisation.ToString("0.0", inv)}");
        sb.AppendLine($"No-show rate %,{totals.NoShowRate.ToString("0.0", inv)}");
        sb.AppendLine();

        sb.AppendLine("Daily summary");
        sb.AppendLine("Date,Weekday,Reservations,Completed,Cancelled,No-show,Walk-ins,Utilisation %");
        for (var d = ds.Start; d < ds.End; d = d.AddDays(1))
        {
            var next = d.AddDays(1);
            var rs = ds.InRange.Where(r => r.ReservedAt >= d && r.ReservedAt < next).ToList();
            var walk = filter.IncludeWalkIns
                ? ds.Sessions.Count(s => s.ReservationId == null && s.OpenedAt >= d && s.OpenedAt < next) : 0;
            var avail = an.DayAvail.GetValueOrDefault(d);
            var util = avail > 0 ? an.DayOcc.GetValueOrDefault(d) / avail * 100 : 0;
            sb.AppendLine(string.Join(",",
                d.ToString("yyyy-MM-dd"), d.DayOfWeek,
                rs.Count,
                rs.Count(r => r.Status == ReservationStatus.Completed),
                rs.Count(r => r.Status == ReservationStatus.Cancelled),
                rs.Count(r => r.Status == ReservationStatus.NoShow),
                walk,
                avail > 0 ? util.ToString("0.0", inv) : "closed"));
        }
        sb.AppendLine();

        sb.AppendLine("Per table");
        sb.AppendLine("Table,Type,Area,Seats,Reservations,Walk-ins,Occupied hours,Utilisation %");
        foreach (var t in BuildTableRows(ds, an))
            sb.AppendLine(string.Join(",", Csv(t.Name), Csv(t.TypeName), Csv(t.AreaName), t.Capacity,
                t.Bookings, t.WalkIns, t.OccupiedHours.ToString("0.0", inv), t.Percent.ToString("0.0", inv)));
        sb.AppendLine();

        sb.AppendLine("Peak hours (average % of tables in use)");
        sb.AppendLine("Weekday," + string.Join(",", heat.Hours.Select(HourLabel)));
        for (var wd = 0; wd < 7; wd++)
            sb.AppendLine(DayNames[wd] + "," + string.Join(",",
                heat.Rows[wd].Select(c => c.Percent.HasValue ? c.Percent.Value.ToString("0.0", inv) : "closed")));

        return sb.ToString();
    }

    // ---------- loading ----------

    private async Task<DataSet> LoadAsync(OccupancyFilter f, CancellationToken ct)
    {
        var ds = new DataSet { Filter = f, Start = f.From.Date, End = f.To.Date.AddDays(1), Now = DateTime.Now };

        var tableRows = await _db.RestaurantTables.AsNoTracking()
            .Where(t => t.IsActive && t.DiningArea.IsActive)
            .Where(t => f.AreaId == null || t.DiningAreaId == f.AreaId)
            .Where(t => f.TableTypeId == null || t.TableTypeDefinitionId == f.TableTypeId)
            .Select(t => new
            {
                t.Id, t.TableNumber, t.Capacity, t.Type,
                Area = t.DiningArea.Name,
                TypeName = t.TableTypeDefinition != null ? t.TableTypeDefinition.Name : null
            })
            .ToListAsync(ct);

        ds.Tables = tableRows
            .OrderBy(t => NaturalKey(t.TableNumber))
            .Select(t => new TableInfo(t.Id, t.TableNumber, t.TypeName ?? t.Type.ToString(), t.Area, t.Capacity))
            .ToList();

        var ids = ds.Tables.Select(t => t.Id).ToList();
        var filtered = f.AreaId != null || f.TableTypeId != null;
        var from = ds.Start.AddDays(-1);        // a booking that began last night can still be running

        var resRaw = await _db.Reservations.AsNoTracking()
            .Where(r => r.ReservedAt >= from && r.ReservedAt < ds.End)
            .Where(r => !filtered || (r.TableId != null && ids.Contains(r.TableId.Value)))
            .Select(r => new { r.Id, r.TableId, r.ReservedAt, r.DurationMinutes, r.Guests, r.Status })
            .ToListAsync(ct);
        var allRes = resRaw
            .Select(r => new ResRow(r.Id, r.TableId, r.ReservedAt, r.DurationMinutes, r.Guests, r.Status)).ToList();
        ds.InRange = allRes.Where(r => r.ReservedAt >= ds.Start).ToList();

        var sessRaw = await _db.DiningSessions.AsNoTracking()
            .Where(s => s.OpenedAt < ds.End && (s.ClosedAt == null || s.ClosedAt >= from))
            .Where(s => ids.Contains(s.TableId))
            .Select(s => new { s.Id, s.TableId, s.ReservationId, s.OpenedAt, s.ClosedAt })
            .ToListAsync(ct);
        ds.Sessions = sessRaw
            .Select(s => new SessRow(s.Id, s.TableId, s.ReservationId, s.OpenedAt, s.ClosedAt)).ToList();

        ds.Hours = await _db.OperatingHours.AsNoTracking().ToListAsync(ct);
        ds.Specials = await _db.SpecialDates.AsNoTracking().ToListAsync(ct);

        // ---- turn everything into "table X was occupied from A to B" ----
        var guestsOf = allRes.ToDictionary(r => r.Id, r => r.Guests);
        var withSession = new HashSet<int>();

        if (f.IncludeWalkIns)
        {
            foreach (var s in ds.Sessions)
            {
                // a session still open is counted up to now, never into the future
                var end = s.ClosedAt ?? (ds.Now > s.OpenedAt ? ds.Now : s.OpenedAt);
                if (end <= s.OpenedAt) continue;
                var guests = s.ReservationId.HasValue && guestsOf.TryGetValue(s.ReservationId.Value, out var g) ? g : 0;
                ds.Intervals.Add(new Interval(s.TableId, s.OpenedAt, end, s.ReservationId == null, guests));
                if (s.ReservationId.HasValue) withSession.Add(s.ReservationId.Value);
            }
        }

        foreach (var r in allRes)
        {
            if (r.TableId == null || !CountsAsBooked(r.Status)) continue;
            if (withSession.Contains(r.Id)) continue;       // the real session already covers it
            ds.Intervals.Add(new Interval(r.TableId.Value, r.ReservedAt, r.ReservedAt.AddMinutes(r.Duration), false, r.Guests));
        }

        return ds;
    }

    // ---------- the maths ----------

    private static Analysis Analyse(DataSet ds)
    {
        var a = new Analysis();
        var tableCount = ds.Tables.Count;
        var days = new List<(DateTime Day, List<Seg> Segs)>();

        for (var d = ds.Start; d < ds.End; d = d.AddDays(1))
        {
            var segs = UpTo(Segments(d, ds.Hours, ds.Specials), ds.Now);   // hours that have not happened yet are not counted
            days.Add((d, segs));
            a.DayAvail[d] = 0;
            a.DayOcc[d] = 0;
            foreach (var seg in segs)
            {
                var mins = (seg.E - seg.S).TotalMinutes;
                a.OpenMinutesPerTable += mins;
                a.DayAvail[d] += mins * tableCount;
                Spread(seg.S, seg.E, a.Avail, tableCount);
            }
        }

        // occupied minutes: merge each table's intervals first so a table can never be more than 100% busy
        var byTable = ds.Intervals.GroupBy(i => i.TableId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var t in ds.Tables)
        {
            double tableOcc = 0;
            if (byTable.TryGetValue(t.Id, out var list))
            {
                var merged = Merge(list);
                foreach (var (day, segs) in days)
                    foreach (var seg in segs)
                        foreach (var m in merged)
                        {
                            if (m.E <= seg.S) continue;
                            if (m.S >= seg.E) break;
                            var s = Max(m.S, seg.S);
                            var e = Min(m.E, seg.E);
                            if (e <= s) continue;
                            var mins = (e - s).TotalMinutes;
                            tableOcc += mins;
                            a.DayOcc[day] += mins;
                            Spread(s, e, a.Occ, 1);
                        }
            }
            a.TableOcc[t.Id] = tableOcc;
        }

        // how many bookings touch each weekday + hour (every booking counted once per hour it runs through)
        var dayIndex = days.ToDictionary(x => x.Day, x => x.Segs);
        foreach (var iv in ds.Intervals)
        {
            for (var d = iv.Start.Date.AddDays(-1); d <= iv.End.Date; d = d.AddDays(1))
            {
                if (!dayIndex.TryGetValue(d, out var segs)) continue;
                foreach (var seg in segs)
                {
                    var s = Max(iv.Start, seg.S);
                    var e = Min(iv.End, seg.E);
                    if (e > s) CountHours(s, e, a.Cnt);
                }
            }
            if (iv.Start >= ds.Start && iv.Start < ds.End && iv.Start < ds.Now)
            {
                if (iv.WalkIn) a.TableWalkIns[iv.TableId] = a.TableWalkIns.GetValueOrDefault(iv.TableId) + 1;
                else a.TableBookings[iv.TableId] = a.TableBookings.GetValueOrDefault(iv.TableId) + 1;
            }
        }
        return a;
    }

    private static OccupancyTotals BuildTotals(DataSet ds, Analysis a)
    {
        var rs = ds.InRange;
        var completed = rs.Count(r => r.Status == ReservationStatus.Completed);
        var cancelled = rs.Count(r => r.Status == ReservationStatus.Cancelled);
        var noShow = rs.Count(r => r.Status == ReservationStatus.NoShow);
        var active = rs.Count(r => r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated);

        // "due" = the booking time has passed and it was not cancelled or still waiting for the restaurant
        var due = rs.Count(r => r.ReservedAt <= ds.Now &&
            (r.Status is ReservationStatus.Confirmed or ReservationStatus.Seated
                or ReservationStatus.Completed or ReservationStatus.NoShow));

        var walkIns = ds.Filter.IncludeWalkIns
            ? ds.Sessions.Count(s => s.ReservationId == null && s.OpenedAt >= ds.Start && s.OpenedAt < ds.End) : 0;

        var booked = rs.Where(r => CountsAsBooked(r.Status)).ToList();
        var avgGuests = booked.Count > 0 ? booked.Average(r => (double)r.Guests) : 0;

        var closed = ds.Sessions
            .Where(s => s.ClosedAt != null && s.OpenedAt >= ds.Start && s.OpenedAt < ds.End
                        && (ds.Filter.IncludeWalkIns || s.ReservationId != null))
            .Select(s => (s.ClosedAt!.Value - s.OpenedAt).TotalMinutes).ToList();
        var avgDwell = closed.Count > 0 ? closed.Average() : 0;

        double occ = 0, avail = 0;
        foreach (var kv in a.DayAvail) avail += kv.Value;
        foreach (var kv in a.DayOcc) occ += kv.Value;

        return new OccupancyTotals(
            rs.Count, completed, cancelled, noShow, active, due, walkIns,
            avail > 0 ? Math.Round(Math.Min(100, occ / avail * 100), 1) : 0,
            due > 0 ? Math.Round(noShow * 100.0 / due, 1) : 0,
            Math.Round(avgGuests, 1), Math.Round(avgDwell, 0),
            ds.Tables.Count, Math.Round(a.OpenMinutesPerTable / 60.0, 1));
    }

    private static OccupancyHeatmap BuildHeatmap(Analysis a)
    {
        var used = Enumerable.Range(0, 24)
            .Where(h => Enumerable.Range(0, 7).Any(d => a.Avail[d, h] > 0)).ToList();
        List<int> hours;
        if (used.Count == 0) hours = new List<int>();
        else if (used.Any(h => h < 6) && used.Any(h => h >= 12))
        {
            // closes after midnight: evening hours first, then the small hours (a break in the middle shows as "closed")
            var late = used.Where(h => h >= 6).Min();
            var early = used.Where(h => h < 6).Max();
            hours = Enumerable.Range(late, 24 - late).Concat(Enumerable.Range(0, early + 1)).ToList();
        }
        else hours = Enumerable.Range(used.Min(), used.Max() - used.Min() + 1).ToList();    // the daily break stays as a closed column

        var rows = new List<IReadOnlyList<OccupancyHeatCell>>();
        for (var d = 0; d < 7; d++)
        {
            var cells = new List<OccupancyHeatCell>();
            foreach (var h in hours)
            {
                double? pct = a.Avail[d, h] > 0
                    ? Math.Round(Math.Min(100, a.Occ[d, h] / a.Avail[d, h] * 100), 1) : null;
                cells.Add(new OccupancyHeatCell(pct, a.Cnt[d, h]));
            }
            rows.Add(cells);
        }
        return new OccupancyHeatmap(hours, rows);
    }

    private static List<OccupancyWeek> BuildWeeks(DataSet ds, Analysis a)
    {
        var weeks = new List<OccupancyWeek>();
        for (var ws = ds.Start; ws < ds.End; ws = ws.AddDays(7))
        {
            var we = ws.AddDays(7) > ds.End ? ds.End : ws.AddDays(7);      // we is the day after the last day
            var n = (int)(we - ws).TotalDays;
            var res = ds.InRange.Count(r => r.ReservedAt >= ws && r.ReservedAt < we);
            var walk = ds.Filter.IncludeWalkIns
                ? ds.Sessions.Count(s => s.ReservationId == null && s.OpenedAt >= ws && s.OpenedAt < we) : 0;
            double occ = 0, avail = 0;
            for (var d = ws; d < we; d = d.AddDays(1))
            {
                occ += a.DayOcc.GetValueOrDefault(d);
                avail += a.DayAvail.GetValueOrDefault(d);
            }
            weeks.Add(new OccupancyWeek(ws, we.AddDays(-1), n, n < 7, res, walk,
                avail > 0 ? Math.Round(Math.Min(100, occ / avail * 100), 1) : 0));
        }
        return weeks;
    }

    private static List<OccupancyTableRow> BuildTableRows(DataSet ds, Analysis a)
    {
        return ds.Tables.Select(t =>
        {
            var occ = a.TableOcc.GetValueOrDefault(t.Id);
            var pct = a.OpenMinutesPerTable > 0 ? Math.Min(100, occ / a.OpenMinutesPerTable * 100) : 0;
            return new OccupancyTableRow(t.Id, t.Name, t.TypeName, t.AreaName, t.Capacity,
                Math.Round(pct, 1), a.TableBookings.GetValueOrDefault(t.Id), a.TableWalkIns.GetValueOrDefault(t.Id),
                Math.Round(occ / 60.0, 1));
        })
        .OrderByDescending(r => r.Percent).ThenBy(r => NaturalKey(r.Name)).ToList();
    }

    private static OccupancyWindow? FindBusiest(OccupancyHeatmap heat)
    {
        double best = 0; int bd = -1, bh = -1;
        for (var d = 0; d < 7; d++)
            for (var i = 0; i < heat.Hours.Count; i++)
            {
                var p = heat.Rows[d][i].Percent;
                if (p.HasValue && p.Value > best) { best = p.Value; bd = d; bh = i; }
            }
        return bd < 0 ? null : new OccupancyWindow(bd, bd, heat.Hours[bh], heat.Hours[bh] + 1, best);
    }

    // The quietest box, widened to the neighbouring days and hours that are almost as quiet (within 10 points).
    private static OccupancyWindow? FindQuietest(OccupancyHeatmap heat)
    {
        double low = double.MaxValue; int ld = -1, lh = -1;
        for (var d = 0; d < 7; d++)
            for (var i = 0; i < heat.Hours.Count; i++)
            {
                var p = heat.Rows[d][i].Percent;
                if (p.HasValue && p.Value < low) { low = p.Value; ld = d; lh = i; }
            }
        if (ld < 0) return null;

        var limit = low + 10;
        bool Quiet(int d, int i) => heat.Rows[d][i].Percent is double p && p <= limit;

        int d0 = ld, d1 = ld;
        while (d0 > 0 && Quiet(d0 - 1, lh)) d0--;
        while (d1 < 6 && Quiet(d1 + 1, lh)) d1++;

        bool HourQuiet(int i) { for (var d = d0; d <= d1; d++) if (!Quiet(d, i)) return false; return true; }
        int h0 = lh, h1 = lh;
        while (h0 > 0 && HourQuiet(h0 - 1)) h0--;
        while (h1 < heat.Hours.Count - 1 && HourQuiet(h1 + 1)) h1++;

        return new OccupancyWindow(d0, d1, heat.Hours[h0], heat.Hours[h1] + 1, low);
    }

    // ---------- helpers ----------

    // Opening periods of one day, with special dates and the break taken out.
    private static List<Seg> Segments(DateTime day, List<OperatingHour> hours, List<SpecialDate> specials)
    {
        var list = new List<Seg>();
        var sp = specials.FirstOrDefault(s => s.Date.Date <= day && (s.EndDate ?? s.Date).Date >= day);
        if (sp != null)
        {
            if (sp.IsClosed || !sp.OpenTime.HasValue || !sp.CloseTime.HasValue) return list;
            var so = day + sp.OpenTime.Value;
            var sc = day + sp.CloseTime.Value;
            if (sc <= so) sc = sc.AddDays(1);
            list.Add(new Seg(so, sc));
            return list;
        }

        var h = hours.FirstOrDefault(x => x.Day == day.DayOfWeek);
        if (h == null || h.IsClosed) return list;

        var open = day + h.OpenTime.ToTimeSpan();
        var close = day + h.CloseTime.ToTimeSpan();
        if (close <= open) close = close.AddDays(1);

        if (h.BreakStart.HasValue && h.BreakEnd.HasValue)
        {
            var bs = day + h.BreakStart.Value.ToTimeSpan();
            var be = day + h.BreakEnd.Value.ToTimeSpan();
            if (be > bs && bs >= open && be <= close)
            {
                if (bs > open) list.Add(new Seg(open, bs));
                if (close > be) list.Add(new Seg(be, close));
                return list;
            }
        }
        list.Add(new Seg(open, close));
        return list;
    }

    private static List<Seg> Merge(List<Interval> intervals)
    {
        var sorted = intervals.OrderBy(i => i.Start).ToList();
        var result = new List<Seg>();
        foreach (var i in sorted)
        {
            if (result.Count > 0 && i.Start <= result[^1].E)
            {
                if (i.End > result[^1].E) result[^1] = result[^1] with { E = i.End };
            }
            else result.Add(new Seg(i.Start, i.End));
        }
        return result;
    }

    // Adds the minutes of [s, e) to the weekday + hour boxes it passes through.
    private static void Spread(DateTime s, DateTime e, double[,] target, double multiplier)
    {
        var cur = s;
        while (cur < e)
        {
            var next = new DateTime(cur.Year, cur.Month, cur.Day, cur.Hour, 0, 0).AddHours(1);
            if (next > e) next = e;
            target[WeekdayIndex(cur.DayOfWeek), cur.Hour] += (next - cur).TotalMinutes * multiplier;
            cur = next;
        }
    }

    private static void CountHours(DateTime s, DateTime e, int[,] target)
    {
        var cur = s;
        while (cur < e)
        {
            var next = new DateTime(cur.Year, cur.Month, cur.Day, cur.Hour, 0, 0).AddHours(1);
            target[WeekdayIndex(cur.DayOfWeek), cur.Hour]++;
            cur = next;
        }
    }

    private static bool CountsAsBooked(ReservationStatus s) =>
        s is ReservationStatus.Confirmed or ReservationStatus.Seated or ReservationStatus.Completed;

    private static int WeekdayIndex(DayOfWeek d) => ((int)d + 6) % 7;      // Monday = 0 ... Sunday = 6
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    private static string HourLabel(int h) => $"{(h % 12 == 0 ? 12 : h % 12)} {(h < 12 ? "AM" : "PM")}";

    // "T2" sorts before "T10"
    private static string NaturalKey(string s) =>
        Regex.Replace(s ?? "", @"\d+", m => m.Value.PadLeft(6, '0'));

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
