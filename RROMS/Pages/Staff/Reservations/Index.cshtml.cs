using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Staff.Reservations;

// Owner: Member 2 / US16 (Staff starting page: Today's reservations). Pages call services only.
// /Staff is already protected by the StaffOrAdmin folder policy in Program.cs; it is repeated here
// so the page stays protected even if the folder rule is ever moved.
[Authorize(Policy = "StaffOrAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(IStaffReservationService staff, ILogger<IndexModel> logger) : PageModel
{
    public StaffDayBoard Board { get; private set; } = null!;
    public string ViewMode { get; private set; } = "ledger";

    public DateTime Today => DateTime.Today;
    public string DateText => Board.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string PrevDate => Board.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string NextDate => Board.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task OnGetAsync(string? date, string? view, CancellationToken cancellationToken)
    {
        ViewMode = string.Equals(view, "timeline", StringComparison.OrdinalIgnoreCase) ? "timeline" : "ledger";
        Board = await staff.GetDayBoardAsync(ParseDate(date), cancellationToken);
    }

    // Auto-refresh: only the part of the page that changes (summary, list, timeline).
    public async Task<IActionResult> OnGetBoardAsync(string? date, CancellationToken cancellationToken)
        => Partial("_Board", await staff.GetDayBoardAsync(ParseDate(date), cancellationToken));

    // "View details" pop-up.
    public async Task<IActionResult> OnGetDetailsAsync(int id, CancellationToken cancellationToken)
    {
        var details = await staff.GetDetailsAsync(id, cancellationToken);
        return details is null ? NotFound() : Partial("_Details", details);
    }

    // ---------- actions (all return JSON: { success, code, message }) ----------

    public Task<IActionResult> OnPostConfirmAsync(int id, CancellationToken ct)
        => RunAsync("confirm", id, (uid, role) => staff.ConfirmAsync(id, uid, role, ct), ct);

    public Task<IActionResult> OnPostSeatAsync(int id, CancellationToken ct)
        => RunAsync("seat", id, (uid, role) => staff.SeatAsync(id, uid, role, ct), ct);

    public Task<IActionResult> OnPostNoShowAsync(int id, CancellationToken ct)
        => RunAsync("mark as no-show", id, (uid, role) => staff.NoShowAsync(id, uid, role, ct), ct);

    public Task<IActionResult> OnPostCancelAsync(int id, string? reason, CancellationToken ct)
        => RunAsync("cancel", id, (uid, role) => staff.CancelAsync(id, uid, role, reason, ct), ct);

    public Task<IActionResult> OnPostCompleteAsync(int id, CancellationToken ct)
        => RunAsync("complete", id, (uid, role) => staff.CompleteAsync(id, uid, role, ct), ct);

    public Task<IActionResult> OnPostModifyAsync(int id, string? date, string? time, int guests, int tableId,
        string? specialRequest, CancellationToken ct)
        => RunAsync("update", id, (uid, role) =>
            staff.ModifyAsync(id, new StaffModifyRequest(ParseDate(date), time ?? "", guests, tableId, specialRequest), uid, role, ct), ct);

    // Data for the "Modify" dialog: time slots and which tables are free for the chosen date / time / guests.
    public async Task<IActionResult> OnGetOptionsAsync(int id, string? date, string? time, int guests,
        CancellationToken ct)
    {
        var options = await staff.GetEditOptionsAsync(id, ParseDate(date), time, guests, ct);
        if (options is null) return NotFound();
        return new JsonResult(new
        {
            closed = options.Closed,
            closedReason = options.ClosedReason,
            time = options.Time,
            slots = options.Slots.Select(x => new { value = x.Value, label = x.Label }),
            tables = options.Tables.Select(x => new { id = x.Id, label = x.Label, area = x.Area, capacity = x.Capacity, free = x.Free, note = x.Note })
        });
    }

    private async Task<IActionResult> RunAsync(string verb, int id,
        Func<string, string, Task<StaffActionResult>> action, CancellationToken ct)
    {
        StaffActionResult result;
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var role = User.IsInRole(Roles.Admin) ? Roles.Admin : Roles.Staff;
            result = await action(userId, role);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "Could not {Action} reservation {ReservationId}", verb, id);
            return new JsonResult(new
            {
                success = false,
                code = "error",
                message = "We could not confirm the result. Refresh the page and check this booking before trying again."
            })
            { StatusCode = 503 };
        }

        int status = result.Success ? 200 : result.Code == "not_found" ? 404 : 409;
        return new JsonResult(new { success = result.Success, code = result.Code, message = result.Message })
        { StatusCode = status };
    }

    private static DateTime ParseDate(string? date)
    {
        if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && d.Year is >= 2020 and <= 2100)
            return d.Date;
        return DateTime.Today;
    }
}

// Small display helpers shared by the page and its two partial views.
public static class StaffLedgerFormat
{
    public static string Clock(DateTime at) => at.ToString("h:mm tt", CultureInfo.InvariantCulture);

    public static string HourLabel(int hour) => new DateTime(2000, 1, 1).AddHours(hour % 24)
        .ToString("h tt", CultureInfo.InvariantCulture);

    // tooltip on a timeline table: "Standard · 6 seats"
    public static string LaneTip(StaffTableLane l)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.TypeName)) parts.Add(l.TypeName!);
        if (l.Capacity > 0) parts.Add($"{l.Capacity} {(l.Capacity == 1 ? "seat" : "seats")}");
        return string.Join(" · ", parts);
    }

    // "6 d 23 h", "2 h 10 min", "12 min": the time left until a pending booking is cancelled automatically.
    public static string RemainingShort(DateTime until, DateTime now)
    {
        var mins = (int)Math.Max(0, Math.Ceiling((until - now).TotalMinutes));
        var d = mins / 1440;
        var h = mins % 1440 / 60;
        var m = mins % 60;
        if (d > 0) return h > 0 ? $"{d} d {h} h" : $"{d} d";
        if (h > 0) return m > 0 ? $"{h} h {m} min" : $"{h} h";
        return $"{mins} min";
    }

    // The same, as big numbers with small units for the details pop-up.
    public static Microsoft.AspNetCore.Html.IHtmlContent RemainingParts(DateTime until, DateTime now)
    {
        var mins = (int)Math.Max(0, Math.Ceiling((until - now).TotalMinutes));
        var d = mins / 1440;
        var h = mins % 1440 / 60;
        var m = mins % 60;
        static string P(int v, string u) => $"<b>{v}</b><small>{u}</small>";
        var html = d > 0 ? P(d, d == 1 ? "day" : "days") + (h > 0 ? P(h, "h") : "")
                 : h > 0 ? P(h, "h") + (m > 0 ? P(m, "min") : "")
                 : P(mins, "min");
        return new Microsoft.AspNetCore.Html.HtmlString(html);
    }

    // "2026-11-04:1,2026-11-06:2" for the date picker (days with pending bookings get a dot).
    public static string PendingDaysData(StaffDayBoard b) =>
        string.Join(',', b.PendingDays.Select(x => x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ":" + x.Count));

    public static string Plural(int n, string one, string many) => n == 1 ? $"{n} {one}" : $"{n} {many}";

    // "bucket" is what the filter tabs look at; a late booking also carries the "late" flag.
    public static string Bucket(StaffReservationRow r) => r.Status switch
    {
        RROMS.Models.ReservationStatus.Pending => "pending",
        RROMS.Models.ReservationStatus.Confirmed => "confirmed",
        RROMS.Models.ReservationStatus.Seated => "seated",
        RROMS.Models.ReservationStatus.Completed => "completed",
        _ => "cancelled"
    };

    public static string StatusText(StaffReservationRow r) => r.Status switch
    {
        RROMS.Models.ReservationStatus.NoShow => "No-show",
        _ => r.Status.ToString()
    };

    // Visual state. A late booking is shown as "late" instead of its plain status.
    public static string Visual(StaffReservationRow r) =>
        r.IsLate ? "late" : Bucket(r);

    public static bool IsAllergy(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        new[] { "allerg", "peanut", "shellfish", "gluten", "lactose", "nut free", "nut-free", "intoleran" }
            .Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    public static string RequestKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (IsAllergy(text)) return "warn";
        return new[] { "birthday", "anniversary", "cake", "celebrat", "proposal", "surprise" }
            .Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)) ? "cake" : "note";
    }

    // The timeline always starts and ends on a whole hour and also covers any booking outside opening hours.
    public static (DateTime Start, DateTime End) TimelineRange(StaffDayBoard b)
    {
        var start = b.Hours.Open ?? b.Date.AddHours(11);
        var end = b.Hours.Close ?? b.Date.AddHours(23);
        foreach (var r in b.Rows)
        {
            if (r.ReservedAt < start) start = r.ReservedAt;
            if (r.EndsAt > end) end = r.EndsAt;
        }
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0);
        if (end.Minute != 0 || end.Second != 0)
            end = new DateTime(end.Year, end.Month, end.Day, end.Hour, 0, 0).AddHours(1);
        if (end <= start) end = start.AddHours(1);
        return (start, end);
    }

    public static string Pct(DateTime at, DateTime start, DateTime end) =>
        Math.Clamp((at - start).TotalMinutes / (end - start).TotalMinutes * 100, 0, 100)
            .ToString("0.###", CultureInfo.InvariantCulture);

    public static string PctWidth(DateTime from, DateTime to, DateTime start, DateTime end)
    {
        var a = Math.Max(0, (from - start).TotalMinutes);
        var b = Math.Min((end - start).TotalMinutes, (to - start).TotalMinutes);
        return (Math.Max(0, b - a) / (end - start).TotalMinutes * 100).ToString("0.###", CultureInfo.InvariantCulture);
    }

    // Text used by the search box (lower case, one string per booking).
    public static string SearchText(StaffReservationRow r) =>
        string.Join(' ', new[]
        {
            r.GuestName, r.ReferenceNo, r.Phone, r.TableNumber, r.AreaName, r.TableTypeName, r.SpecialRequest
        }.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

    // Small line icons (24x24, drawn with currentColor) so the page needs no icon font.
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M12 7v5l3 2\"/>",
        ["calendar"] = "<rect x=\"3.5\" y=\"5\" width=\"17\" height=\"15\" rx=\"2\"/><path d=\"M3.5 10h17M8 3v4m8-4v4\"/>",
        ["users"] = "<circle cx=\"9\" cy=\"8\" r=\"3.2\"/><path d=\"M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6M16 5.5a3 3 0 0 1 0 5.5M18.5 14.5c1.6.8 2.5 2.7 2.5 5.5\"/>",
        ["table"] = "<path d=\"M4 9.5h16M6 9.5v10m12-10v10M3 9.5l2-4h14l2 4\"/>",
        ["pin"] = "<path d=\"M12 21s7-6.2 7-11.5A7 7 0 0 0 5 9.5C5 14.8 12 21 12 21Z\"/><circle cx=\"12\" cy=\"9.5\" r=\"2.5\"/>",
        ["coin"] = "<ellipse cx=\"12\" cy=\"7\" rx=\"7\" ry=\"3\"/><path d=\"M5 7v5c0 1.7 3.1 3 7 3s7-1.3 7-3V7M5 12v5c0 1.7 3.1 3 7 3s7-1.3 7-3v-5\"/>",
        ["phone"] = "<path d=\"M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2Z\"/>",
        ["copy"] = "<rect x=\"8\" y=\"8\" width=\"12\" height=\"12\" rx=\"2\"/><path d=\"M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2\"/>",
        ["eye"] = "<path d=\"M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>",
        ["check"] = "<path d=\"m5 12.5 4.2 4.2L19 7\"/>",
        ["x"] = "<path d=\"M6 6l12 12M18 6 6 18\"/>",
        ["warn"] = "<path d=\"M12 3 2 21h20L12 3Zm0 6v6m0 3v.5\"/>",
        ["cake"] = "<path d=\"M5 20h14v-7H5v7Zm0-4c2 1.5 3 1.5 4.5 0s3-1.5 4.5 0 3 1.5 5 0M12 13V9m0-4v1\"/>",
        ["note"] = "<path d=\"M6 3h12v18H6V3Zm3 5h6m-6 4h6m-6 4h3\"/>",
        ["seat"] = "<path d=\"M6 19V8a3 3 0 0 1 3-3h6a3 3 0 0 1 3 3v11M4 13h16M8 19v-3m8 3v-3\"/>",
        ["noshow"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"m6 6 12 12\"/>",
        ["refresh"] = "<path d=\"M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8\"/><path d=\"M21 3v5h-5\"/>",
        ["search"] = "<circle cx=\"11\" cy=\"11\" r=\"6.5\"/><path d=\"m16 16 4.5 4.5\"/>",
        ["alert"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M12 7.5v5.5m0 3v.4\"/>",
        ["left"] = "<path d=\"m14.5 5-7 7 7 7\"/>",
        ["right"] = "<path d=\"m9.5 5 7 7-7 7\"/>",
        ["more"] = "<circle cx=\"5\" cy=\"12\" r=\"1.4\"/><circle cx=\"12\" cy=\"12\" r=\"1.4\"/><circle cx=\"19\" cy=\"12\" r=\"1.4\"/>",
        ["cup"] = "<path d=\"M5 8h11v6a4 4 0 0 1-4 4H9a4 4 0 0 1-4-4V8Zm11 1h2a2 2 0 0 1 0 5h-2M8 3v2m3-2v2\"/>",
        ["list"] = "<path d=\"M9 6h11M9 12h11M9 18h11\"/><circle cx=\"4.5\" cy=\"6\" r=\"1\"/><circle cx=\"4.5\" cy=\"12\" r=\"1\"/><circle cx=\"4.5\" cy=\"18\" r=\"1\"/>",
        ["info"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 11v6m0-9.5v.5\"/>",
        ["gift"] = "<rect x=\"4\" y=\"10\" width=\"16\" height=\"10\" rx=\"1.5\"/><path d=\"M3 7h18v3H3zM12 7v13M12 7c-2.5 0-4-1-4-2.5S9.5 2.5 12 7zm0 0c2.5 0 4-1 4-2.5S14.5 2.5 12 7z\"/>",
        ["edit"] = "<path d=\"M4 20h4L19 9l-4-4L4 16v4Zm9-13 4 4\"/>",
        ["person"] = "<circle cx=\"12\" cy=\"8\" r=\"3.5\"/><path d=\"M5 20c0-3.9 3.1-7 7-7s7 3.1 7 7\"/>"
    };

    public static Microsoft.AspNetCore.Html.IHtmlContent Icon(string name) =>
        new Microsoft.AspNetCore.Html.HtmlString(
            "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\">" +
            (Paths.TryGetValue(name, out var p) ? p : "") + "</svg>");
}
