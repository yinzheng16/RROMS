// Owner: Member 2 (Tables & Reservations) - US20 table occupancy and reservation analytics.
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Occupancy;

// Role protection for /Admin is done by Program.cs, so no [Authorize] is needed here.
public class IndexModel : PageModel
{
    private const int MaxDays = 366;

    private readonly IOccupancyAnalyticsService _service;
    private readonly IWebHostEnvironment _env;

    public IndexModel(IOccupancyAnalyticsService service, IWebHostEnvironment env)
    {
        _service = service;
        _env = env;
    }

    // ---- what the admin picked (all optional, all in the address so the page can be bookmarked) ----
    [BindProperty(Name = "from", SupportsGet = true)] public string? FromText { get; set; }
    [BindProperty(Name = "to", SupportsGet = true)] public string? ToText { get; set; }
    [BindProperty(Name = "range", SupportsGet = true)] public int? Range { get; set; }
    [BindProperty(Name = "area", SupportsGet = true)] public int? AreaId { get; set; }
    [BindProperty(Name = "type", SupportsGet = true)] public int? TableTypeId { get; set; }
    [BindProperty(Name = "mode", SupportsGet = true)] public string? Mode { get; set; }

    public OccupancyReport Report { get; private set; } = null!;
    public OccupancyChoices Choices { get; private set; } =
        new(new List<OccupancyChoice>(), new List<OccupancyChoice>(), new Dictionary<int, IReadOnlyList<int>>());
    public OccupancyFilter Filter { get; private set; } = null!;
    public DateTime Today { get; private set; }
    public int ActivePreset { get; private set; }         // 7, 30, 90, or 0 for a custom range
    public string? TypeNotice { get; private set; }        // the chosen table type is not in the chosen area
    public string? FilterNotice { get; private set; }      // shown in red under the filter when the dates make no sense
    // true when anything other than the plain default view is chosen, so the filter panel stays open and Reset is in reach
    public bool FilterActive =>
        FilterNotice != null || TypeNotice != null || AreaId.HasValue || TableTypeId.HasValue
        || string.Equals(Mode, "res", StringComparison.OrdinalIgnoreCase) || ActivePreset == 0;
    public bool IsDevelopment => _env.IsDevelopment();
    public bool HasData => Report.Totals.Reservations > 0 || Report.Totals.WalkIns > 0;

    [TempData] public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Filter = await PrepareAsync(ct);
        Report = await _service.BuildAsync(Filter, ct);
    }

    // Click on one heatmap box: /Admin/Occupancy?handler=Slot&day=5&hour=19&from=...
    public async Task<IActionResult> OnGetSlotAsync(int day, int hour, CancellationToken ct)
    {
        if (day < 0 || day > 6 || hour < 0 || hour > 23) return BadRequest();
        var d = await _service.SlotAsync(await PrepareAsync(ct), day, hour, ct);
        return new JsonResult(new
        {
            day = d.Day,
            hour = d.Hour,
            percent = d.Percent,
            reservations = d.Reservations,
            walkIns = d.WalkIns,
            avgGuests = d.AvgGuests,
            daysCounted = d.DaysCounted,
            topTables = d.TopTables.Select(t => new { name = t.Name, type = t.TypeName, hours = t.Hours }),
            busiestDate = d.BusiestDate?.ToString("yyyy-MM-dd"),
            busiestDateText = d.BusiestDate?.ToString("ddd d MMM", CultureInfo.InvariantCulture),
            busiestDateReservations = d.BusiestDateReservations,
            busiestDateWalkIns = d.BusiestDateWalkIns,
            // only offer the link when the staff page exists in this build and that day has real reservations to open
            staffUrl = d.BusiestDate.HasValue && d.BusiestDateReservations > 0
                ? Url.Page("/Staff/Reservations/Index", new { date = d.BusiestDate.Value.ToString("yyyy-MM-dd") })
                : null
        });
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        var filter = await PrepareAsync(ct);
        var csv = await _service.ExportCsvAsync(filter, ct);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();      // BOM so Excel reads it
        return File(bytes, "text/csv; charset=utf-8", $"occupancy_{filter.From:yyyyMMdd}_{filter.To:yyyyMMdd}.csv");
    }

    // ---- Development only: sample data ----
    public async Task<IActionResult> OnPostLoadSampleAsync(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        var n = await _service.LoadSampleAsync(userId, ct);
        SuccessMessage = n > 0 ? $"Sample data added ({n} records, last 90 days)." : "There are no active tables to build sample data for.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearSampleAsync(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        var n = await _service.ClearSampleAsync(ct);
        SuccessMessage = $"Sample data removed ({n} records).";
        return RedirectToPage();
    }

    // ---- reading the address ----

    // Loads the dropdown choices and drops a table type that has no table in the chosen area.
    private async Task<OccupancyFilter> PrepareAsync(CancellationToken ct)
    {
        Choices = await _service.ChoicesAsync(ct);
        if (AreaId.HasValue && TableTypeId.HasValue)
        {
            Choices.AreaTypes.TryGetValue(AreaId.Value, out var ids);
            if (ids == null || !ids.Contains(TableTypeId.Value))
            {
                TableTypeId = null;
                TypeNotice = "That table type has no tables in the chosen dining area, so all table types are shown.";
            }
        }
        return ReadFilter();
    }

    private OccupancyFilter ReadFilter()
    {
        Today = DateTime.Today;
        DateTime from, to;
        var keepInput = false;

        if (Range is 7 or 30 or 90)
        {
            to = Today;
            from = Today.AddDays(-(Range.Value - 1));
        }
        else if (TryDate(FromText, out var f) && TryDate(ToText, out var t))
        {
            if (f > t)
            {
                // do not guess what the admin meant: say so, and show the last 30 days meanwhile
                FilterNotice = "The To date can't be earlier than the From date. Showing the last 30 days instead.";
                keepInput = true;
                to = Today;
                from = Today.AddDays(-29);
            }
            else
            {
                from = f; to = t;
                if (to > Today) to = Today;                // the future has no history yet
                if (from > to) from = to;
            }
        }
        else
        {
            to = Today;
            from = Today.AddDays(-29);
        }

        if ((to - from).TotalDays + 1 > MaxDays) from = to.AddDays(-(MaxDays - 1));

        ActivePreset = to == Today
            ? (int)((to - from).TotalDays + 1) switch { 7 => 7, 30 => 30, 90 => 90, _ => 0 }
            : 0;
        if (!keepInput)
        {
            FromText = from.ToString("yyyy-MM-dd");
            ToText = to.ToString("yyyy-MM-dd");
        }

        // "res" = reservations only. Anything else counts reservations and walk-ins.
        var walkIns = !string.Equals(Mode, "res", StringComparison.OrdinalIgnoreCase);
        return new OccupancyFilter(from, to, AreaId, TableTypeId, walkIns);
    }

    private static bool TryDate(string? text, out DateTime date) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    // ---- helpers the page uses ----
    public static readonly string[] DayShort = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    public static readonly string[] DayLong = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    public static string Hour12(int h) => $"{(h % 12 == 0 ? 12 : h % 12)} {(h < 12 ? "AM" : "PM")}";
    public static string HourShort(int h) => $"{(h % 12 == 0 ? 12 : h % 12)}{(h < 12 ? "a" : "p")}";

    // 5 -> "Saturday", range 1..3 -> "Tue–Thu"
    public static string DayText(int from, int to) => from == to ? DayLong[from] : DayShort[from] + "–" + DayShort[to];

    // 15..17 -> "3–5 PM", 11..13 -> "11 AM–1 PM"
    public static string HourRange(int from, int to)
    {
        var a = from % 12 == 0 ? 12 : from % 12;
        var b = to % 12 == 0 ? 12 : to % 12;
        var aSuffix = from < 12 ? "AM" : "PM";
        var bSuffix = to % 24 < 12 ? "AM" : "PM";
        return aSuffix == bSuffix ? $"{a}–{b} {bSuffix}" : $"{a} {aSuffix}–{b} {bSuffix}";
    }

    public static int Level(double? p) => p switch
    {
        null => -1,
        < 5 => 0,
        < 20 => 1,
        < 40 => 2,
        < 60 => 3,
        < 80 => 4,
        _ => 5
    };

    public string PageUrl(string? from = null, string? to = null, int? range = null, string? mode = null,
                       int? area = null, int? type = null, string? handler = null) =>
        Url.Page("/Admin/Occupancy/Index", new
        {
            handler,
            from, to, range,
            area = area ?? AreaId,
            type = type ?? TableTypeId,
            mode = mode ?? (Filter.IncludeWalkIns ? null : "res")
        })!;
}
