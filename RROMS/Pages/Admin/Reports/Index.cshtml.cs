using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Reports;

// US44 Filtered reports (Member 5). Admin only (the /Admin folder is protected in Program.cs).
// Everything the admin picks lives in the address (?type=orders&st=Completed…),
// so Back, Refresh, bookmarks, the CSV and the PDF all show exactly the same report.
public class Index : PageModel
{
    public static readonly int[] PageSizes = { 10, 25, 50, 100 };

    [BindProperty(SupportsGet = true, Name = "per")]
    public int Per { get; set; } = 25;
    private const int MaxDays = 366;

    private readonly ISalesReportService _reports;

    public Index(ISalesReportService reports)
    {
        _reports = reports;
    }

    // ---------- Read from the address ----------

    [BindProperty(SupportsGet = true, Name = "type")]
    public string? TypeKey { get; set; }

    [BindProperty(SupportsGet = true, Name = "from")]
    public string? FromText { get; set; }

    [BindProperty(SupportsGet = true, Name = "to")]
    public string? ToText { get; set; }

    [BindProperty(SupportsGet = true, Name = "st")]
    public List<string> Groups { get; set; } = new();

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Customer { get; set; }

    [BindProperty(SupportsGet = true, Name = "kind")]
    public string? Kind { get; set; }

    [BindProperty(SupportsGet = true, Name = "day")]
    public string? DayText { get; set; }

    [BindProperty(SupportsGet = true, Name = "sort")]
    public int? SortColumn { get; set; }

    [BindProperty(SupportsGet = true, Name = "dir")]
    public string? SortDir { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNo { get; set; } = 1;

    [BindProperty(SupportsGet = true, Name = "all")]
    public bool ShowAll { get; set; }

    [BindProperty(SupportsGet = true, Name = "cmp")]
    public string? CompareText { get; set; }

    // ---------- For the page ----------

    public ReportResult Report { get; private set; } = new();
    public ReportQuery Query { get; private set; } = new();
    public List<ReportRow> PageRows { get; private set; } = new();
    public int PageCount { get; private set; } = 1;
    public bool IsPrint { get; private set; }
    public string? Notice { get; private set; }

    public static readonly (string Key, string Kanji, string Name)[] Types =
    {
        ("reservations", "予約", "Reservations"),
        ("orders", "注文", "Orders"),
        ("sales", "売上", "Sales"),
        ("menu", "品目", "Menu items")
    };

    // Quick date ranges (US45). Kept in the address as ?range=week, so a bookmark of
    // "This week" still means this week next time. Custom ranges use ?from=…&to=….
    public static readonly (string Key, string Name)[] Presets =
    {
        ("today", "Today"), ("week", "This week"), ("month", "This month"),
        ("7d", "Last 7 days"), ("30d", "Last 30 days"), ("90d", "Last 90 days")
    };

    private const string DefaultRange = "7d";

    [BindProperty(SupportsGet = true, Name = "range")]
    public string? RangeKey { get; set; }

    // The quick range in use (US45), or null for a custom range.
    public string? ActiveRange { get; private set; }

    // Start and end of a quick range. Weeks start on Monday.
    public static (DateTime From, DateTime To) RangeOf(string key, DateTime today) => key switch
    {
        "today" => (today, today),
        "week" => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
        "month" => (new DateTime(today.Year, today.Month, 1), today),
        "30d" => (today.AddDays(-29), today),
        "90d" => (today.AddDays(-89), today),
        _ => (today.AddDays(-6), today)
    };

    public string PeriodName => ActiveRange == null ? "Custom range" : Presets.First(p => p.Key == ActiveRange).Name;

    public bool HasFilters => Query.Groups.Count > 0 || !string.IsNullOrWhiteSpace(Query.Customer)
                              || !string.IsNullOrEmpty(Query.Kind) || Query.Day.HasValue;

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    // Export → PDF: the same report on one A4 sheet. The browser's print window saves it as a PDF.
    public async Task<IActionResult> OnGetPrintAsync()
    {
        IsPrint = true;
        await LoadAsync();
        return Page();
    }

    // Export → CSV: every matching row plus the total row. Opens in Excel.
    public async Task<IActionResult> OnGetCsvAsync()
    {
        await LoadAsync();

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Report.Columns.Select(c => Csv(c.Title))));
        foreach (var row in Report.Rows)
        {
            sb.AppendLine(string.Join(",", row.Cells.Select(c => Csv(c.Text))));
        }
        sb.AppendLine(string.Join(",", Report.Total.Cells.Select(c => Csv(c.Text))));

        // UTF-8 with a BOM so Excel shows "×" and "—" correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var name = $"yoru-{Types[(int)Query.Type].Key}-{Query.From:yyyy-MM-dd}-to-{Query.To:yyyy-MM-dd}.csv";
        return File(bytes, "text/csv", name);
    }

    private async Task LoadAsync()
    {
        Query = BuildQuery();
        Report = await _reports.BuildAsync(Query);

        if (!PageSizes.Contains(Per)) Per = 25;
        PageCount = Math.Max(1, (int)Math.Ceiling(Report.Rows.Count / (double)Per));
        PageNo = Math.Clamp(PageNo, 1, PageCount);
        PageRows = IsPrint
            ? Report.Rows
            : Report.Rows.Skip((PageNo - 1) * Per).Take(Per).ToList();
    }

    // Turns the address into a safe ReportQuery (bad or missing values fall back to defaults).
    private ReportQuery BuildQuery()
    {
        var q = new ReportQuery();

        var index = Array.FindIndex(Types, t => t.Key == TypeKey);
        q.Type = index >= 0 ? (ReportType)index : ReportType.Orders;

        var today = DateTime.Today;
        DateTime from, to;
        var customFrom = ParseDate(FromText);
        var customTo = ParseDate(ToText);
        if (Presets.Any(p => p.Key == RangeKey) || (customFrom == null && customTo == null))
        {
            // Quick range (or nothing chosen yet: last 7 days).
            ActiveRange = Presets.Any(p => p.Key == RangeKey) ? RangeKey : DefaultRange;
            (from, to) = RangeOf(ActiveRange!, today);
        }
        else
        {
            ActiveRange = null;
            from = customFrom ?? customTo!.Value;
            to = customTo ?? customFrom!.Value;
            if (to < from)
            {
                (from, to) = (to, from);
            }
            if ((to - from).Days + 1 > MaxDays)
            {
                from = to.AddDays(-(MaxDays - 1));
                Notice = $"Reports can cover up to {MaxDays} days, so the start date was moved to {from:d MMM yyyy}.";
            }
        }
        q.From = from;
        q.To = to;
        SetComparison(q);

        q.Groups = Groups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).Distinct().Take(20).ToList();
        q.Customer = string.IsNullOrWhiteSpace(Customer) ? null : Customer.Trim()[..Math.Min(Customer.Trim().Length, 100)];
        q.Kind = string.IsNullOrWhiteSpace(Kind) || q.Type is ReportType.Reservations or ReportType.Menu ? null : Kind.Trim();

        var day = ParseDate(DayText);
        q.Day = day.HasValue && day.Value >= from && day.Value <= to ? day : null;

        q.SortColumn = SortColumn;
        q.SortDescending = SortDir == "desc";
        // Comparing with an earlier period is on unless the address says cmp=off.
        q.Compare = CompareText != "off";
        return q;
    }

    // What each range is compared with (US45: "compare performance over time").
    private void SetComparison(ReportQuery q)
    {
        switch (ActiveRange)
        {
            case "today":
                q.CompareLabel = "yesterday";
                q.CompareSpan = "yesterday";
                break;
            case "week":
                // Monday to today vs Monday to the same weekday last week.
                q.CompareFrom = q.From.AddDays(-7);
                q.CompareTo = q.To.AddDays(-7);
                q.CompareLabel = "same days last week";
                q.CompareSpan = "the same days last week";
                break;
            case "month":
                // 1st to today vs 1st to the same date last month (or its last day).
                var lastMonth = q.From.AddMonths(-1);
                var lastDay = Math.Min(q.To.Day, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month));
                q.CompareFrom = lastMonth;
                q.CompareTo = new DateTime(lastMonth.Year, lastMonth.Month, lastDay);
                q.CompareLabel = "same days last month";
                q.CompareSpan = "the same days last month";
                break;
            default:
                q.CompareLabel = q.Days == 1 ? "the day before" : $"previous {q.Days} days";
                q.CompareSpan = q.Days == 1 ? "the day before" : $"the {q.Days} days before";
                break;
        }
    }

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;

    // Quotes a CSV value, and stops Excel from treating text like "=SUM(...)" as a formula.
    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !decimal.TryParse(value, out _))
        {
            value = "'" + value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // =====================================================================
    // Links: each one is the current report with one thing changed.
    // =====================================================================

    private Dictionary<string, List<string>> State()
    {
        var d = new Dictionary<string, List<string>>();
        void Set(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) d[key] = new List<string> { value };
        }

        Set("type", Types[(int)Query.Type].Key);
        if (ActiveRange != null)
        {
            if (ActiveRange != DefaultRange) Set("range", ActiveRange);
        }
        else
        {
            Set("from", Query.From.ToString("yyyy-MM-dd"));
            Set("to", Query.To.ToString("yyyy-MM-dd"));
        }
        if (Query.Groups.Count > 0) d["st"] = Query.Groups.ToList();
        Set("q", Query.Customer);
        Set("kind", Query.Kind);
        Set("day", Query.Day?.ToString("yyyy-MM-dd"));
        Set("sort", Query.SortColumn?.ToString());
        if (Query.SortColumn.HasValue && Query.SortDescending) Set("dir", "desc");
        if (PageNo > 1) Set("p", PageNo.ToString());
        if (ShowAll) Set("all", "true");
        if (Per != 25) Set("per", Per.ToString());
        if (!Query.Compare) Set("cmp", "off");
        return d;
    }

    private static string Build(Dictionary<string, List<string>> d, string? handler = null)
    {
        var parts = new List<string>();
        if (handler != null) parts.Add("handler=" + handler);
        foreach (var (key, values) in d)
        {
            parts.AddRange(values.Select(v => key + "=" + Uri.EscapeDataString(v)));
        }
        return "/Admin/Reports" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }

    // Most changes start again from page 1.
    private string Link(Action<Dictionary<string, List<string>>> change, bool keepPage = false)
    {
        var d = State();
        if (!keepPage) d.Remove("p");
        change(d);
        return Build(d);
    }

    public string ReportLink(string key) => Link(d =>
    {
        d["type"] = new() { key };
        foreach (var k in new[] { "st", "kind", "sort", "dir", "day" }) d.Remove(k);
    });

    public string PresetLink(string key) => Link(d =>
    {
        d.Remove("from");
        d.Remove("to");
        d.Remove("day");
        if (key == DefaultRange) d.Remove("range"); else d["range"] = new() { key };
    });

    public bool IsPreset(string key) => ActiveRange == key;

    public string GroupLink(string name) => Link(d =>
    {
        var list = d.TryGetValue("st", out var l) ? l : new List<string>();
        if (!list.Remove(name)) list.Add(name);
        if (list.Count > 0) d["st"] = list; else d.Remove("st");
    });

    public string CustomerLink(string name) => Link(d =>
    {
        if (Query.Customer == name) d.Remove("q"); else d["q"] = new() { name };
    });

    public string DayLink(DateTime day) => Link(d =>
    {
        if (Query.Day == day.Date) d.Remove("day"); else d["day"] = new() { day.ToString("yyyy-MM-dd") };
    });

    public string RemoveLink(string key, string? value = null) => Link(d =>
    {
        if (value != null && d.TryGetValue(key, out var list))
        {
            list.Remove(value);
            if (list.Count == 0) d.Remove(key);
        }
        else
        {
            d.Remove(key);
        }
    });

    public string ResetLink() => Link(d =>
    {
        foreach (var k in new[] { "st", "q", "kind", "day" }) d.Remove(k);
    });

    public string SortLink(int column) => Link(d =>
    {
        var desc = Query.SortColumn == column && !Query.SortDescending;
        d["sort"] = new() { column.ToString() };
        if (desc) d["dir"] = new() { "desc" }; else d.Remove("dir");
    });

    public string PageLink(int page) => Link(d =>
    {
        if (page > 1) d["p"] = new() { page.ToString() }; else d.Remove("p");
    });

    public string ShowAllLink() => Link(d =>
    {
        if (ShowAll) d.Remove("all"); else d["all"] = new() { "true" };
    });

    public string CompareLink() => Link(d =>
    {
        if (Query.Compare) d["cmp"] = new() { "off" }; else d.Remove("cmp");
    }, keepPage: true);

    public string CsvLink() => Build(State(), "Csv");
    public string PrintLink() => Build(State(), "Print");
    public string BackLink() => Build(State());

    // Hidden fields so the search / type / date forms keep everything else.
    public IEnumerable<KeyValuePair<string, string>> HiddenFields(params string[] except)
    {
        foreach (var (key, values) in State())
        {
            // The custom-range form replaces the quick range, so it drops ?range too.
            if (except.Contains(key) || key == "p" || (key == "range" && except.Contains("from"))) continue;
            foreach (var v in values) yield return new(key, v);
        }
    }

    public string PeriodText => Query.From == Query.To
        ? Query.From.ToString("d MMM yyyy")
        : Query.From.Year == Query.To.Year
            ? $"{Query.From:d MMM} – {Query.To:d MMM yyyy}"
            : $"{Query.From:d MMM yyyy} – {Query.To:d MMM yyyy}";

    public string CompareSpan => Query.CompareSpan;
}