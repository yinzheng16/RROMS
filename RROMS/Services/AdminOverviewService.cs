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

    public AdminOverviewService(
        IRepository<Reservation> reservationRepository,
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository)
    {
        _reservationRepository = reservationRepository;
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public async Task<AdminOverviewData> GetOverviewAsync()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var data = new AdminOverviewData();

        data.TodayReservationCount = await _reservationRepository
            .Query()
            .CountAsync(r => r.ReservedAt >= today &&
                             r.ReservedAt < tomorrow);

        data.TodayOrderCount = await _orderRepository
            .Query()
            .CountAsync(o => o.CreatedAt >= today &&
                             o.CreatedAt < tomorrow);

        data.TodayPaymentCount = await _paymentRepository
            .Query()
            .CountAsync(p => p.PaidAt.HasValue &&
                             p.PaidAt.Value >= today &&
                             p.PaidAt.Value < tomorrow);

        data.TodayPaymentTotal = await _paymentRepository
            .Query()
            .Where(p => p.PaidAt.HasValue &&
                        p.PaidAt.Value >= today &&
                        p.PaidAt.Value < tomorrow)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

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
}