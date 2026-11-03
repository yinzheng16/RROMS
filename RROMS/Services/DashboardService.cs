using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 5 (US46 Interactive dashboard).
    // Works out the cards, charts and highlights for Pages/Admin/Dashboard.
    // Uses the same rules as the reports (US44), so the numbers always match:
    //   net sales = paid − refunded payments (by payment date)
    //   orders    = orders that were not cancelled (by order date)
    // Read-only: this service never changes any data.
    public class DashboardService : IDashboardService
    {
        private static readonly CultureInfo My = CultureInfo.GetCultureInfo("en-MY");

        private readonly IRepository<Reservation> _reservations;
        private readonly IRepository<Order> _orders;
        private readonly IRepository<Payment> _payments;
        private readonly IRepository<OrderItem> _orderItems;

        public DashboardService(
            IRepository<Reservation> reservations,
            IRepository<Order> orders,
            IRepository<Payment> payments,
            IRepository<OrderItem> orderItems)
        {
            _reservations = reservations;
            _orders = orders;
            _payments = payments;
            _orderItems = orderItems;
        }

        public async Task<DashboardResult> BuildAsync(DashboardQuery q)
        {
            var from = q.From.Date;
            var end = q.To.Date.AddDays(1);
            var prevFrom = q.CompareFrom.Date;
            var prevEnd = q.CompareTo.Date.AddDays(1);
            var loadFrom = prevFrom < from ? prevFrom : from;

            // ---------- Load both periods in one trip each ----------
            var payments = await _payments.Query().AsNoTracking()
                .Where(p => (p.PaidAt ?? p.CreatedAt) >= loadFrom && (p.PaidAt ?? p.CreatedAt) < end)
                .Select(p => new { Date = p.PaidAt ?? p.CreatedAt, p.Amount, p.Status })
                .ToListAsync();

            var orders = await _orders.Query().AsNoTracking()
                .Where(o => o.CreatedAt >= loadFrom && o.CreatedAt < end && o.Status != OrderStatus.Cancelled)
                .Select(o => new { o.CreatedAt })
                .ToListAsync();

            var reservations = await _reservations.Query().AsNoTracking()
                .Where(r => r.ReservedAt >= loadFrom && r.ReservedAt < end)
                .Select(r => new { r.ReservedAt, r.Guests, r.Status })
                .ToListAsync();

            var dishes = await _orderItems.Query().AsNoTracking()
                .Where(i => i.Order.CreatedAt >= from && i.Order.CreatedAt < end
                            && i.Order.Status != OrderStatus.Cancelled)
                .GroupBy(i => i.MenuItem.Name)
                .Select(g => new { Name = g.Key, Portions = g.Sum(i => i.Quantity) })
                .OrderByDescending(d => d.Portions)
                .Take(5)
                .ToListAsync();

            bool InNow(DateTime d) => d >= from && d < end;
            bool InPrev(DateTime d) => d >= prevFrom && d < prevEnd;

            decimal Net(IEnumerable<(DateTime Date, decimal Amount, PaymentStatus Status)> ps) =>
                ps.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount)
                - ps.Where(p => p.Status == PaymentStatus.Refunded).Sum(p => p.Amount);

            var pay = payments.Select(p => (p.Date, p.Amount, p.Status)).ToList();
            var payNow = pay.Where(p => InNow(p.Date)).ToList();
            var payPrev = pay.Where(p => InPrev(p.Date)).ToList();

            var resNow = reservations.Where(r => InNow(r.ReservedAt)).ToList();
            var resPrev = reservations.Where(r => InPrev(r.ReservedAt)).ToList();
            int Guests(IEnumerable<int> g) => g.Sum();
            var guestsNow = Guests(resNow.Where(r => r.Status != ReservationStatus.Cancelled).Select(r => r.Guests));
            var guestsPrev = Guests(resPrev.Where(r => r.Status != ReservationStatus.Cancelled).Select(r => r.Guests));

            var ordersNow = orders.Count(o => InNow(o.CreatedAt));
            var ordersPrev = orders.Count(o => InPrev(o.CreatedAt));

            var result = new DashboardResult
            {
                Query = q,
                Paid = payNow.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount),
                Refunded = payNow.Where(p => p.Status == PaymentStatus.Refunded).Sum(p => p.Amount),
                Guests = guestsNow
            };

            // ---------- Cards ----------
            // No comparison when the earlier period has no data at all (e.g. before the system went live).
            var hasPrev = payPrev.Count > 0 || resPrev.Count > 0 || ordersPrev > 0;
            result.Kpis.Add(Kpi("Net sales", "純売上", Net(payNow), Net(payPrev), hasPrev, money: true));
            result.Kpis.Add(Kpi("Orders", "注文数", ordersNow, ordersPrev, hasPrev));
            result.Kpis.Add(Kpi("Reservations", "予約数", resNow.Count, resPrev.Count, hasPrev));
            result.Kpis.Add(Kpi("Guests", "来店人数", guestsNow, guestsPrev, hasPrev));

            // ---------- One entry per day for the line and bar charts ----------
            var shift = (from - prevFrom).Days;
            var statuses = Enum.GetValues<ReservationStatus>();
            for (var day = from; day < end; day = day.AddDays(1))
            {
                var prevDay = day.AddDays(-shift);
                var d = new DashboardDay
                {
                    Day = day,
                    NetSales = Net(payNow.Where(p => p.Date.Date == day)),
                    Orders = orders.Count(o => o.CreatedAt.Date == day),
                    PreviousDay = prevDay,
                    PreviousNetSales = InPrev(prevDay) ? Net(payPrev.Where(p => p.Date.Date == prevDay)) : 0,
                    PreviousOrders = InPrev(prevDay) ? orders.Count(o => o.CreatedAt.Date == prevDay) : 0
                };
                foreach (var s in statuses)
                {
                    d.Reservations[SalesReportService.Label(s)] = resNow.Count(r => r.ReservedAt.Date == day && r.Status == s);
                }
                result.Days.Add(d);
            }

            // Only the statuses that actually appear, in a sensible order.
            foreach (var s in statuses)
            {
                var name = SalesReportService.Label(s);
                if (result.Days.Any(d => d.Reservations[name] > 0))
                {
                    result.ReservationStatuses.Add(new DashboardStatus { Name = name, Color = ColorOf(s) });
                }
            }

            // ---------- Popular dishes ----------
            result.TopDishes.AddRange(dishes.Select(d => new DashboardDish { Name = d.Name, Portions = d.Portions }));

            // ---------- Highlights ----------
            var best = result.Days.OrderByDescending(d => d.NetSales).FirstOrDefault();
            result.Highlights.Add(best != null && best.NetSales > 0
                ? new DashboardHighlight { Mark = "売", Color = "#C9392B", Title = $"Best day: {best.Day:ddd d MMM}", Detail = Rm(best.NetSales) + " net sales" }
                : new DashboardHighlight { Mark = "売", Color = "#C9392B", Title = "No sales yet", Detail = "Try a longer date range" });

            var top = result.TopDishes.FirstOrDefault();
            result.Highlights.Add(top != null
                ? new DashboardHighlight { Mark = "品", Color = "#1E1A18", Title = $"Top dish: {top.Name}", Detail = $"{top.Portions} portions sold" }
                : new DashboardHighlight { Mark = "品", Color = "#1E1A18", Title = "No dishes sold yet", Detail = "Orders will show here" });

            var busiest = resNow.Where(r => r.Status != ReservationStatus.Cancelled)
                .GroupBy(r => r.ReservedAt.DayOfWeek)
                .Select(g => new { Day = g.Key, Guests = g.Sum(r => r.Guests) })
                .OrderByDescending(g => g.Guests)
                .FirstOrDefault();
            result.Highlights.Add(busiest != null
                ? new DashboardHighlight { Mark = "客", Color = "#3D5A80", Title = $"Busiest weekday: {busiest.Day}", Detail = $"{busiest.Guests} guests booked" }
                : new DashboardHighlight { Mark = "客", Color = "#3D5A80", Title = "No reservations yet", Detail = "Bookings will show here" });

            // No-show rate among reservations that are already over.
            var finished = resNow.Count(r => r.Status is ReservationStatus.Completed or ReservationStatus.NoShow or ReservationStatus.Cancelled);
            var noShows = resNow.Count(r => r.Status == ReservationStatus.NoShow);
            result.Highlights.Add(new DashboardHighlight
            {
                Mark = "欠",
                Color = "#7E2318",
                Title = $"No-show rate: {(finished == 0 ? 0 : Math.Round(noShows * 100m / finished))}%",
                Detail = $"{noShows} of {finished} finished reservations"
            });

            return result;
        }

        private static DashboardKpi Kpi(string label, string jp, decimal now, decimal before, bool hasPrev, bool money = false) => new()
        {
            Label = label,
            JpLabel = jp,
            Value = now,
            IsMoney = money,
            Change = hasPrev && before != 0 ? (int)Math.Round((now - before) * 100 / before) : null
        };

        private static string Rm(decimal v) => "RM " + v.ToString("N2", My);

        // Same colours as the reports (US44).
        private static string ColorOf(ReservationStatus s) => s switch
        {
            ReservationStatus.Completed => "#3F7A4C",
            ReservationStatus.Confirmed or ReservationStatus.Seated => "#3D5A80",
            ReservationStatus.Pending => "#C9A24A",
            ReservationStatus.Cancelled => "#C9392B",
            ReservationStatus.NoShow => "#7E2318",
            _ => "#B79B72"
        };
    }
}