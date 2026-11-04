using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;
using ReportsPage = RROMS.Pages.Admin.Reports.Index;

namespace RROMS.Pages.Admin;

// US46 Interactive dashboard (Member 5). Page: /Admin/Dashboard. Admin only (the /Admin folder is protected in Program.cs).
// Uses the same date ranges as the reports (US45), so ?range=week means the same on both pages.
public class DashboardModel : PageModel
{
    private const string DefaultRange = "7d";
    private const int MaxDays = 366;

    private readonly IDashboardService _dashboard;

    public DashboardModel(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [BindProperty(SupportsGet = true, Name = "range")]
    public string? RangeKey { get; set; }

    [BindProperty(SupportsGet = true, Name = "from")]
    public string? FromText { get; set; }

    [BindProperty(SupportsGet = true, Name = "to")]
    public string? ToText { get; set; }

    public DashboardResult Result { get; private set; } = new();

    // The quick range in use, or null for a custom range.
    public string? ActiveRange { get; private set; }

    public static (string Key, string Name)[] Presets => ReportsPage.Presets;

    public string PeriodName => ActiveRange == null ? "Custom range" : Presets.First(p => p.Key == ActiveRange).Name;

    public string PeriodText
    {
        get
        {
            var q = Result.Query;
            return q.From == q.To
                ? q.From.ToString("d MMM yyyy")
                : q.From.Year == q.To.Year
                    ? $"{q.From:d MMM} – {q.To:d MMM yyyy}"
                    : $"{q.From:d MMM yyyy} – {q.To:d MMM yyyy}";
        }
    }

    public async Task OnGetAsync()
    {
        Result = await _dashboard.BuildAsync(BuildQuery());
    }

    private DashboardQuery BuildQuery()
    {
        var today = DateTime.Today;
        var q = new DashboardQuery();
        var customFrom = ParseDate(FromText);
        var customTo = ParseDate(ToText);

        if (Presets.Any(p => p.Key == RangeKey) || (customFrom == null && customTo == null))
        {
            ActiveRange = Presets.Any(p => p.Key == RangeKey) ? RangeKey : DefaultRange;
            (q.From, q.To) = ReportsPage.RangeOf(ActiveRange!, today);
        }
        else
        {
            ActiveRange = null;
            var from = customFrom ?? customTo!.Value;
            var to = customTo ?? customFrom!.Value;
            if (to < from) (from, to) = (to, from);
            if ((to - from).Days + 1 > MaxDays) from = to.AddDays(-(MaxDays - 1));
            q.From = from;
            q.To = to;
        }

        // What the period is compared with (same rules as the reports, US45).
        var days = (q.To - q.From).Days + 1;
        switch (ActiveRange)
        {
            case "today":
                q.CompareFrom = q.CompareTo = q.From.AddDays(-1);
                q.CompareLabel = "yesterday";
                break;
            case "week":
                q.CompareFrom = q.From.AddDays(-7);
                q.CompareTo = q.To.AddDays(-7);
                q.CompareLabel = "same days last week";
                break;
            case "month":
                var lastMonth = q.From.AddMonths(-1);
                q.CompareFrom = lastMonth;
                q.CompareTo = new DateTime(lastMonth.Year, lastMonth.Month,
                    Math.Min(q.To.Day, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month)));
                q.CompareLabel = "same days last month";
                break;
            default:
                q.CompareTo = q.From.AddDays(-1);
                q.CompareFrom = q.From.AddDays(-days);
                q.CompareLabel = days == 1 ? "the day before" : $"previous {days} days";
                break;
        }
        return q;
    }

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;

    // ---------- Links ----------

    public string PresetLink(string key) =>
        key == DefaultRange ? "/Admin/Dashboard" : "/Admin/Dashboard?range=" + Uri.EscapeDataString(key);

    // The same period on the Reports page (optionally one report type and one day).
    public string ReportLink(string type, DateTime? day = null)
    {
        var parts = new List<string> { "type=" + type };
        if (ActiveRange != null)
        {
            if (ActiveRange != DefaultRange) parts.Add("range=" + ActiveRange);
        }
        else
        {
            parts.Add("from=" + Result.Query.From.ToString("yyyy-MM-dd"));
            parts.Add("to=" + Result.Query.To.ToString("yyyy-MM-dd"));
        }
        if (day.HasValue) parts.Add("day=" + day.Value.ToString("yyyy-MM-dd"));
        return "/Admin/Reports?" + string.Join("&", parts);
    }
}