using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public class AdminOverviewService : IAdminOverviewService
{
    private readonly IRepository<Reservation> _reservationRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<OperatingHour> _operatingHourRepository;
    private readonly IRepository<RestaurantTable> _restaurantTableRepository;
    private readonly IRepository<MenuItem> _menuItemRepository;

    public AdminOverviewService(
        IRepository<Reservation> reservationRepository,
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<OperatingHour> operatingHourRepository,
        IRepository<RestaurantTable> restaurantTableRepository,
        IRepository<MenuItem> menuItemRepository)
    {
        _reservationRepository = reservationRepository;
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _operatingHourRepository = operatingHourRepository;
        _restaurantTableRepository = restaurantTableRepository;
        _menuItemRepository = menuItemRepository;
    }

    public async Task<AdminOverviewData> GetOverviewAsync(OverviewPeriod period = OverviewPeriod.Today)
    {
        var now = DateTime.Now;
        var today = now.Date;
        var tomorrow = today.AddDays(1);

        var data = new AdminOverviewData
        {
            GeneratedAt = now,
            Period = period
        };

        // ---------- Restaurant status ----------
        var todayOperatingHour = await _operatingHourRepository
            .Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Day == today.DayOfWeek);

        if (todayOperatingHour != null)
        {
            data.IsClosedToday = todayOperatingHour.IsClosed;
            data.TodayOpenTime = todayOperatingHour.OpenTime;
            data.TodayCloseTime = todayOperatingHour.CloseTime;
        }
        else
        {
            data.IsClosedToday = true;
        }

        data.ActiveTableCount = await _restaurantTableRepository
            .Query()
            .CountAsync(t => t.IsActive);

        data.TotalSeatCount = await _restaurantTableRepository
            .Query()
            .Where(t => t.IsActive)
            .SumAsync(t => (int?)t.Capacity) ?? 0;

        data.AvailableMenuItemCount = await _menuItemRepository
            .Query()
            .CountAsync(m => m.IsAvailable);

        data.TotalMenuItemCount = await _menuItemRepository
            .Query()
            .CountAsync();

        // ---------- Today ----------
        // Cancelled bookings are not "scheduled", so they are not counted.
        data.TodayReservationCount = await _reservationRepository
            .Query()
            .CountAsync(r => r.ReservedAt >= today &&
                             r.ReservedAt < tomorrow &&
                             r.Status != ReservationStatus.Cancelled);

        data.TodayOrderCount = await _orderRepository
            .Query()
            .CountAsync(o => o.CreatedAt >= today &&
                             o.CreatedAt < tomorrow);

        // Only payments with status Paid count as received (not Refunded).
        var paidToday = _paymentRepository
            .Query()
            .Where(p => p.Status == PaymentStatus.Paid &&
                        p.PaidAt.HasValue &&
                        p.PaidAt.Value >= today &&
                        p.PaidAt.Value < tomorrow);

        data.TodayPaymentCount = await paidToday.CountAsync();
        data.TodayPaymentTotal = await paidToday.SumAsync(p => (decimal?)p.Amount) ?? 0;

        // ---------- Selected period (order tickets) ----------
        var (start, endExclusive) = GetPeriodRange(period, today);
        data.PeriodStart = start;
        data.PeriodEnd = endExclusive.AddDays(-1);

        data.PeriodReservationCount = await _reservationRepository
            .Query()
            .CountAsync(r => r.ReservedAt >= start &&
                             r.ReservedAt < endExclusive &&
                             r.Status != ReservationStatus.Cancelled);

        data.PeriodOrderCount = await _orderRepository
            .Query()
            .CountAsync(o => o.CreatedAt >= start &&
                             o.CreatedAt < endExclusive);

        var paidInPeriod = _paymentRepository
            .Query()
            .Where(p => p.Status == PaymentStatus.Paid &&
                        p.PaidAt.HasValue &&
                        p.PaidAt.Value >= start &&
                        p.PaidAt.Value < endExclusive);

        data.PeriodPaymentCount = await paidInPeriod.CountAsync();
        data.PeriodPaymentTotal = await paidInPeriod.SumAsync(p => (decimal?)p.Amount) ?? 0;

        // ---------- Needs attention (today) ----------
        var pendingReservations = _reservationRepository
            .Query()
            .Where(r => r.ReservedAt >= today &&
                        r.ReservedAt < tomorrow &&
                        r.Status == ReservationStatus.Pending);

        data.PendingReservationCount = await pendingReservations.CountAsync();
        data.FirstPendingReservationAt = await pendingReservations
            .OrderBy(r => r.ReservedAt)
            .Select(r => (DateTime?)r.ReservedAt)
            .FirstOrDefaultAsync();

        // Same rule as the order queue (US25): every Pending order, not only today's.
        var pendingOrders = _orderRepository
            .Query()
            .Where(o => o.Status == OrderStatus.Pending);

        data.PendingOrderCount = await pendingOrders.CountAsync();
        data.OldestPendingOrderNo = await pendingOrders
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.OrderNo)
            .FirstOrDefaultAsync();

        var failedPayments = _paymentRepository
            .Query()
            .Where(p => p.CreatedAt >= today &&
                        p.CreatedAt < tomorrow &&
                        p.Status == PaymentStatus.Failed);

        data.FailedPaymentCount = await failedPayments.CountAsync();
        data.LatestFailedPayment = await failedPayments
            .AsNoTracking()
            .Include(p => p.Order)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        // ---------- Today's reservations ----------
        var activeToday = _reservationRepository
            .Query()
            .Where(r => r.ReservedAt >= today &&
                        r.ReservedAt < tomorrow &&
                        r.Status != ReservationStatus.Cancelled &&
                        r.Status != ReservationStatus.NoShow);

        data.TodayGuestCount = await activeToday.SumAsync(r => (int?)r.Guests) ?? 0;

        // Still to arrive: later today and not seated yet.
        var upcoming = activeToday
            .Where(r => r.ReservedAt >= now &&
                        (r.Status == ReservationStatus.Pending ||
                         r.Status == ReservationStatus.Confirmed));

        data.RemainingReservationCount = await upcoming.CountAsync();
        data.UpcomingReservations = await upcoming
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Table)
            .OrderBy(r => r.ReservedAt)
            .Take(5)
            .ToListAsync();

        // ---------- Latest records ----------
        data.LatestReservations = await _reservationRepository
            .Query()
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Table)
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .ToListAsync();

        data.LatestOrders = await _orderRepository
            .Query()
            .AsNoTracking()
            .Include(o => o.User)
            .OrderByDescending(o => o.CreatedAt)
            .Take(5)
            .ToListAsync();

        data.LatestPayments = await _paymentRepository
            .Query()
            .AsNoTracking()
            .Include(p => p.Order)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5)
            .ToListAsync();

        return data;
    }

    // Today = today only. Week = Monday to today. Month = the 1st to today.
    // The end date is exclusive (tomorrow at 00:00).
    private static (DateTime Start, DateTime EndExclusive) GetPeriodRange(OverviewPeriod period, DateTime today)
    {
        var start = period switch
        {
            OverviewPeriod.Week => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            OverviewPeriod.Month => new DateTime(today.Year, today.Month, 1),
            _ => today
        };

        return (start, today.AddDays(1));
    }
}