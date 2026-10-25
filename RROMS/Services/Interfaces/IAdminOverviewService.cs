using RROMS.Models;

namespace RROMS.Services.Interfaces;

public interface IAdminOverviewService
{
    Task<AdminOverviewData> GetOverviewAsync();
}

public class AdminOverviewData
{
    public int TodayReservationCount { get; set; }
    public int TodayOrderCount { get; set; }
    public int TodayPaymentCount { get; set; }
    public decimal TodayPaymentTotal { get; set; }
    public bool IsClosedToday { get; set; }
    public TimeOnly? TodayOpenTime { get; set; }
    public TimeOnly? TodayCloseTime { get; set; }

    public int ActiveTableCount { get; set; }
    public int TotalSeatCount { get; set; }

    public int AvailableMenuItemCount { get; set; }

    public List<Reservation> LatestReservations { get; set; } = new();
    public List<Order> LatestOrders { get; set; } = new();
    public List<Payment> LatestPayments { get; set; } = new();
}