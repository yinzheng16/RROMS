namespace RROMS.Models
{
    // US46 Interactive dashboard (Member 5).
    // View models for Pages/Admin/Dashboard. Nothing here is saved in the database.

    // The period the admin picked, and the earlier period it is compared with.
    public class DashboardQuery
    {
        public DateTime From { get; set; } = DateTime.Today.AddDays(-6);
        public DateTime To { get; set; } = DateTime.Today;
        public DateTime CompareFrom { get; set; } = DateTime.Today.AddDays(-13);
        public DateTime CompareTo { get; set; } = DateTime.Today.AddDays(-7);

        // e.g. "previous 7 days", "same days last week".
        public string CompareLabel { get; set; } = string.Empty;
    }

    // One of the four cards at the top.
    public class DashboardKpi
    {
        public string Label { get; init; } = string.Empty;
        public string JpLabel { get; init; } = string.Empty;
        public decimal Value { get; init; }
        public bool IsMoney { get; init; }

        // % change against the earlier period; null when there is nothing to compare.
        public int? Change { get; set; }
    }

    // One day on the sales and reservations charts.
    public class DashboardDay
    {
        public DateTime Day { get; init; }
        public decimal NetSales { get; init; }
        public int Orders { get; init; }

        // The matching day of the earlier period (dashed line).
        public DateTime PreviousDay { get; init; }
        public decimal PreviousNetSales { get; init; }
        public int PreviousOrders { get; init; }

        // Reservations that day, by status name.
        public Dictionary<string, int> Reservations { get; } = new();
    }

    public class DashboardDish
    {
        public string Name { get; init; } = string.Empty;
        public int Portions { get; init; }
    }

    public class DashboardStatus
    {
        public string Name { get; init; } = string.Empty;
        public string Color { get; init; } = "#999";
    }

    // A short line in the "Highlights" card.
    public class DashboardHighlight
    {
        public string Mark { get; init; } = string.Empty;    // one kanji, e.g. 売
        public string Color { get; init; } = "#C9392B";
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
    }

    public class DashboardResult
    {
        public DashboardQuery Query { get; init; } = new();

        public List<DashboardKpi> Kpis { get; } = new();
        public List<DashboardDay> Days { get; } = new();
        public List<DashboardDish> TopDishes { get; } = new();
        public List<DashboardStatus> ReservationStatuses { get; } = new();
        public List<DashboardHighlight> Highlights { get; } = new();

        // The dark band: Paid − Refunded = Net sales.
        public decimal Paid { get; init; }
        public decimal Refunded { get; init; }
        public decimal NetSales => Paid - Refunded;
        public int Guests { get; init; }
    }
}