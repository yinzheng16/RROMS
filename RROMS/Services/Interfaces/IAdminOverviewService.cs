using RROMS.Models;

namespace RROMS.Services.Interfaces;

public interface IAdminOverviewService
{
    Task<AdminOverviewData> GetOverviewAsync(OverviewPeriod period = OverviewPeriod.Today);
    
}

// Period for the order tickets: Today / This week / This month.
public enum OverviewPeriod
{
    Today,
    Week,
    Month
}

public class AdminOverviewData
{
    // ---------- When the numbers were loaded ----------
    public DateTime GeneratedAt { get; set; }

    // ---------- Today (used by the receipt and Needs Attention) ----------
    public int TodayReservationCount { get; set; }
    public int TodayOrderCount { get; set; }
    public int TodayPaymentCount { get; set; }
    public decimal TodayPaymentTotal { get; set; }

    // ---------- Selected period (order tickets) ----------
    public OverviewPeriod Period { get; set; } = OverviewPeriod.Today;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }          // last day included
    public int PeriodReservationCount { get; set; }
    public int PeriodOrderCount { get; set; }
    public int PeriodPaymentCount { get; set; }
    public decimal PeriodPaymentTotal { get; set; }

    // ---------- Restaurant status ----------
    public bool IsClosedToday { get; set; }
    public TimeOnly? TodayOpenTime { get; set; }
    public TimeOnly? TodayCloseTime { get; set; }

    public int ActiveTableCount { get; set; }
    public int TotalSeatCount { get; set; }

    public int AvailableMenuItemCount { get; set; }
    public int TotalMenuItemCount { get; set; }
    public int UnavailableMenuItemCount => TotalMenuItemCount - AvailableMenuItemCount;

    // ---------- Needs attention (today) ----------
    public int PendingReservationCount { get; set; }
    public DateTime? FirstPendingReservationAt { get; set; }

    public int PendingOrderCount { get; set; }
    public string? OldestPendingOrderNo { get; set; }

    public int FailedPaymentCount { get; set; }
    public Payment? LatestFailedPayment { get; set; }

    public int AttentionCount => PendingReservationCount + PendingOrderCount + FailedPaymentCount;

    // ---------- Today's reservations ----------
    public int TodayGuestCount { get; set; }
    public int RemainingReservationCount { get; set; }
    public List<Reservation> UpcomingReservations { get; set; } = new();

    // ---------- Latest records ----------
    public List<Reservation> LatestReservations { get; set; } = new();
    public List<Order> LatestOrders { get; set; } = new();
    public List<Payment> LatestPayments { get; set; } = new();
}