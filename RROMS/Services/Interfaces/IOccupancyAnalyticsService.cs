// Owner: Member 2 (Tables & Reservations) - US20 table occupancy and reservation analytics.
namespace RROMS.Services.Interfaces;

/// <summary>What the admin picked on the page. From/To are whole days, both included.</summary>
public record OccupancyFilter(DateTime From, DateTime To, int? AreaId, int? TableTypeId);

/// <summary>One weekday + hour box of the heatmap. Percent is null when the restaurant is never open at that hour.</summary>
public record OccupancyHeatCell(double? Percent, int Bookings);

/// <summary>Rows run Monday to Sunday (index 0 = Monday); Hours are the opening hours shown across the top (0-23).</summary>
public record OccupancyHeatmap(IReadOnlyList<int> Hours, IReadOnlyList<IReadOnlyList<OccupancyHeatCell>> Rows);

/// <summary>One point of the trend line: a block of 7 days counted from the first day of the range.</summary>
public record OccupancyWeek(DateTime Start, DateTime End, int Days, bool IsPartial,
    int Reservations, double UtilisationPercent);

public record OccupancyTableRow(int TableId, string Name, string TypeName, string AreaName, int Capacity,
    double Percent, int Bookings, double OccupiedHours);

/// <summary>A day range and an hour range, e.g. Tue-Thu from 3 PM to 5 PM (ToHour is the hour after the last box).</summary>
public record OccupancyWindow(int FromDay, int ToDay, int FromHour, int ToHour, double Percent);

public record OccupancyTotals(
    int Reservations, int Completed, int Cancelled, int NoShow, int Active, int Due,
    double AvgUtilisation, double NoShowRate, double AvgGuests, double AvgDwellMinutes,
    int TableCount, double OpenHours);

public record OccupancyReport(
    OccupancyFilter Filter,
    OccupancyTotals Totals,
    OccupancyTotals? Previous,
    DateTime? PreviousFrom,
    DateTime? PreviousTo,
    OccupancyHeatmap Heatmap,
    IReadOnlyList<OccupancyWeek> Weeks,
    IReadOnlyList<OccupancyTableRow> Tables,
    OccupancyWindow? Busiest,
    OccupancyWindow? Quietest,
    bool HasSampleData);

public record OccupancySlotTable(string Name, string TypeName, double Hours);

/// <summary>What the page shows when the admin clicks one heatmap box.</summary>
public record OccupancySlotDetail(
    int Day, int Hour, double? Percent, int Reservations, double AvgGuests,
    IReadOnlyList<OccupancySlotTable> TopTables, DateTime? BusiestDate, int BusiestDateReservations, int DaysCounted);

public record OccupancyChoice(int Id, string Name);

/// <summary>AreaTypes: for each dining area, the table types that really have an active table in it.</summary>
public record OccupancyChoices(IReadOnlyList<OccupancyChoice> Areas, IReadOnlyList<OccupancyChoice> TableTypes,
    IReadOnlyDictionary<int, IReadOnlyList<int>> AreaTypes);

public interface IOccupancyAnalyticsService
{
    Task<OccupancyReport> BuildAsync(OccupancyFilter filter, CancellationToken ct = default);
    Task<OccupancySlotDetail> SlotAsync(OccupancyFilter filter, int day, int hour, CancellationToken ct = default);
    Task<string> ExportCsvAsync(OccupancyFilter filter, CancellationToken ct = default);
    Task<OccupancyChoices> ChoicesAsync(CancellationToken ct = default);

    // Development only: sample reservations so the charts are not empty in a demo.
    Task<int> LoadSampleAsync(string userId, CancellationToken ct = default);
    Task<int> ClearSampleAsync(CancellationToken ct = default);
}
