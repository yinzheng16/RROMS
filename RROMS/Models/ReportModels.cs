namespace RROMS.Models
{
    // US44 Filtered reports (Member 5).
    // View models for Pages/Admin/Reports. Nothing here is saved in the database.

    public enum ReportType
    {
        Reservations,
        Orders,
        Sales,
        Menu,
        Customers   // US48 Customer activity
    }

    // What the admin asked for (built from the query string).
    public class ReportQuery
    {
        public ReportType Type { get; set; } = ReportType.Orders;
        public DateTime From { get; set; } = DateTime.Today.AddDays(-6);
        public DateTime To { get; set; } = DateTime.Today;

        // Status names (or category names for the menu report). Empty = all.
        public List<string> Groups { get; set; } = new();

        public string? Customer { get; set; }

        // Order type (Dine-in / Takeaway) or payment method (Cash / Card / Online).
        public string? Kind { get; set; }

        // Set when the admin clicks one bar in the chart.
        public DateTime? Day { get; set; }

        public int? SortColumn { get; set; }
        public bool SortDescending { get; set; }

        // Compare with an earlier period (US45).
        public bool Compare { get; set; } = true;

        public int Days => (To.Date - From.Date).Days + 1;

        // The period to compare with. When not set: the same number of days just before.
        // "This week" compares with the same days last week, "This month" with the same
        // days last month (set by the page, US45).
        public DateTime? CompareFrom { get; set; }
        public DateTime? CompareTo { get; set; }
        public DateTime PreviousFrom => (CompareFrom ?? From.Date.AddDays(-Days)).Date;
        public DateTime PreviousTo => (CompareTo ?? From.Date.AddDays(-1)).Date;

        // Short text after a % change, e.g. "previous 7 days" or "same days last week".
        public string CompareLabel { get; set; } = string.Empty;

        // Longer text for sentences, e.g. "the 7 days before" or "the same days last week".
        public string CompareSpan { get; set; } = string.Empty;
    }

    public class ReportColumn
    {
        public string Title { get; init; } = string.Empty;
        public bool Right { get; init; }
    }

    public class ReportCell
    {
        public string Text { get; init; } = string.Empty;

        // CSS class for a coloured badge (status), or null for plain text.
        public string? Badge { get; init; }

        // 0–100: draws a small bar (menu "share of sales").
        public double? Share { get; init; }

        // Used when the admin sorts by this column.
        public IComparable? Sort { get; init; }
    }

    public class ReportRow
    {
        public string Key { get; init; } = string.Empty;
        public List<ReportCell> Cells { get; } = new();

        // Shown when the row is opened (what was ordered, special request…).
        public List<KeyValuePair<string, string>> Details { get; } = new();

        // Allergy / diet note from the customer's profile (US03).
        public string? Warning { get; set; }

        // Used to look up the dining note after the rows are built.
        public string? UserId { get; init; }
    }

    public class ReportKpi
    {
        public string Label { get; init; } = string.Empty;
        public string Display { get; init; } = string.Empty;
        public decimal Value { get; init; }

        // % change against the period before; null when there is nothing to compare.
        public int? Change { get; set; }

        // True for things that are better when they go down (no-shows, refunds).
        public bool GoodWhenDown { get; init; }

        // True when the value is a name, not a number (best seller).
        public bool IsText { get; init; }
    }

    public class ReportChartPoint
    {
        public DateTime Day { get; init; }
        public decimal Value { get; init; }
        public decimal? Previous { get; init; }
        public string Display { get; init; } = string.Empty;
        public string? PreviousDisplay { get; init; }
    }

    public class ReportGroup
    {
        public string Name { get; init; } = string.Empty;
        public decimal Value { get; init; }
        public string Display { get; init; } = string.Empty;
        public string Color { get; init; } = "#999";
        public bool Selected { get; init; }
    }

    public class ReportResult
    {
        public ReportQuery Query { get; init; } = new();
        public string Title { get; init; } = string.Empty;
        public string Lead { get; set; } = string.Empty;

        public List<ReportKpi> Kpis { get; } = new();

        // Index of the KPI shown in the big red box.
        public int MainKpi { get; init; }

        public string ChartLabel { get; init; } = string.Empty;
        public List<ReportChartPoint> Chart { get; } = new();

        public string GroupLabel { get; init; } = "Status";
        public List<ReportGroup> Groups { get; } = new();

        public string TopLabel { get; init; } = string.Empty;
        public List<KeyValuePair<string, string>> TopCustomers { get; } = new();

        // Order type / payment method choices; empty when the report has none.
        public string KindLabel { get; init; } = string.Empty;
        public List<string> KindOptions { get; } = new();

        public List<string> CustomerNames { get; } = new();

        public List<ReportColumn> Columns { get; } = new();

        // Every row that matches the filters (the page shows them in pages of 10).
        public List<ReportRow> Rows { get; } = new();
        public ReportRow Total { get; set; } = new();

        public string RowNoun { get; init; } = "rows";
    }
}