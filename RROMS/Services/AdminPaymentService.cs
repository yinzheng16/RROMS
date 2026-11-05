using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 4 (US40 - Administrative payment transaction monitoring)
    // Read-only list of every payment for the Admin → Payments page:
    // search, date range, status and method filters, status totals
    // (KPI cards) and daily totals. Amounts are the saved payment
    // amounts; nothing is recalculated.
    public class AdminPaymentService : IAdminPaymentService
    {
        public const int PageSize = 50;

        // RC-000012 / YB-000012 / RC12 / 12
        private static readonly Regex NumberSearch = new(
            @"^(?<prefix>RC|YB)?\s*-?\s*0*(?<id>\d{1,9})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly IRepository<Payment> _payments;
        private readonly IRepository<ApplicationUser> _users;

        public AdminPaymentService(
            IRepository<Payment> payments,
            IRepository<ApplicationUser> users)
        {
            _payments = payments;
            _users = users;
        }

        public async Task<AdminPaymentMonitor> GetPaymentsAsync(AdminPaymentFilter filter)
        {
            ArgumentNullException.ThrowIfNull(filter);

            if (filter.From is DateOnly f && filter.To is DateOnly t && f > t)
                throw new ArgumentException(
                    "The From date cannot be after the To date.");

            // Search, date and method. Status is applied later, so the
            // KPI cards and status chips always show every status.
            IQueryable<Payment> baseQuery = ApplySearch(
                _payments.Query().AsNoTracking(), filter.Search);

            if (filter.From is DateOnly fromDate)
            {
                DateTime start = fromDate.ToDateTime(TimeOnly.MinValue);
                baseQuery = baseQuery.Where(p => (p.PaidAt ?? p.CreatedAt) >= start);
            }

            if (filter.To is DateOnly toDate)
            {
                // Up to the end of the To day: before midnight of the next day.
                DateTime end = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
                baseQuery = baseQuery.Where(p => (p.PaidAt ?? p.CreatedAt) < end);
            }

            if (filter.Method is PaymentMethod method)
                baseQuery = baseQuery.Where(p => p.Method == method);

            // ---------- KPI cards / status chips ----------
            var statusRows = await baseQuery
                .GroupBy(p => p.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count(),
                    Amount = g.Sum(p => p.Amount)
                })
                .ToListAsync();

            var byStatus = statusRows.ToDictionary(
                r => r.Status,
                r => new AdminPaymentStatusTotal(r.Count, r.Amount));

            // ---------- Daily totals (Paid only counts as money received) ----------
            var dayRows = await baseQuery
                .GroupBy(p => (p.PaidAt ?? p.CreatedAt).Date)
                .Select(g => new
                {
                    Day = g.Key,
                    PaymentCount = g.Count(),
                    PaidCount = g.Count(p => p.Status == PaymentStatus.Paid),
                    PaidAmount = g.Sum(p => p.Status == PaymentStatus.Paid ? p.Amount : 0m)
                })
                .ToListAsync();

            var days = dayRows
                .OrderByDescending(d => d.Day)
                .Select(d => new AdminPaymentDayTotal(
                    DateOnly.FromDateTime(d.Day),
                    d.PaymentCount,
                    d.PaidCount,
                    d.PaidAmount))
                .ToList();

            // ---------- The list ----------
            IQueryable<Payment> listQuery = baseQuery;

            if (filter.Status is PaymentStatus status)
                listQuery = listQuery.Where(p => p.Status == status);

            int matches = await listQuery.CountAsync();
            int pageCount = Math.Max(1, (int)Math.Ceiling(matches / (double)PageSize));
            int page = Math.Clamp(filter.Page, 1, pageCount);

            var users = _users.Query();

            var payments = await listQuery
                .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(p => new AdminPaymentRecord
                {
                    PaymentId = p.Id,
                    BillId = p.BillId,

                    OrderType = p.Bill != null
                        ? (p.Bill.DiningSessionId != null
                            ? OrderType.DineIn
                            : OrderType.Takeaway)
                        : p.Order!.OrderType,

                    TableNumber = p.Bill != null
                        ? (p.Bill.DiningSession != null
                            ? p.Bill.DiningSession.Table.TableNumber
                            : null)
                        : (p.Order != null && p.Order.DiningSession != null
                            ? p.Order.DiningSession.Table.TableNumber
                            : null),

                    OrderNo = p.Bill != null
                        ? p.Bill.Orders
                            .OrderBy(o => o.Id)
                            .Select(o => o.OrderNo)
                            .FirstOrDefault()
                        : p.Order!.OrderNo,

                    OrderCount = p.Bill != null
                        ? p.Bill.Orders.Count(o => o.Status != OrderStatus.Cancelled)
                        : 1,

                    // Dine-in: the customer of the dining session.
                    // Takeaway: the customer who placed the order.
                    CustomerName = p.Bill != null
                        ? (p.Bill.DiningSession != null
                            ? p.Bill.DiningSession.Customer!.FullName
                            : p.Bill.Orders
                                .OrderBy(o => o.Id)
                                .Select(o => o.User.FullName)
                                .FirstOrDefault())
                        : p.Order!.User.FullName,

                    Amount = p.Amount,
                    Method = p.Method,
                    Status = p.Status,
                    TransactionRef = p.TransactionRef,

                    RecordedByName = users
                        .Where(u => u.Id == p.RecordedByUserId)
                        .Select(u => u.FullName)
                        .FirstOrDefault(),

                    CreatedAt = p.CreatedAt,
                    PaidAt = p.PaidAt,

                    // For the checks (failed then paid later / paid twice / amount differs).
                    BillStatus = p.Bill != null ? (BillStatus?)p.Bill.Status : null,
                    BillTotal = p.Bill != null ? (decimal?)p.Bill.TotalAmount : null,
                    PaidOnSameBill = p.Bill != null
                        ? p.Bill.Payments.Count(x => x.Status == PaymentStatus.Paid)
                        : p.Order!.Payments.Count(x => x.BillId == null && x.Status == PaymentStatus.Paid)
                })
                .ToListAsync();

            return new AdminPaymentMonitor
            {
                Payments = payments,
                Matches = matches,
                Page = page,
                PageCount = pageCount,
                PageSize = PageSize,
                ByStatus = byStatus,
                Days = days
            };
        }

        // One search box for several things:
        //  - RC-000012 -> payment no., YB-000012 -> bill no.
        //  - a plain number (12) -> payment no., bill no., or part of an
        //    order no. / table number
        //  - any text -> order no., table number, customer name, reference
        private static IQueryable<Payment> ApplySearch(IQueryable<Payment> query, string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return query;

            string s = search.Trim();
            if (s.Length > 100)
                s = s[..100];

            int id = -1;
            var number = NumberSearch.Match(s);
            if (number.Success && int.TryParse(number.Groups["id"].Value, out int parsed))
            {
                string prefix = number.Groups["prefix"].Value.ToUpperInvariant();

                if (prefix == "RC")
                    return query.Where(p => p.Id == parsed);

                if (prefix == "YB")
                    return query.Where(p => p.BillId == parsed);

                id = parsed;
            }

            // id stays -1 for text, which never matches an ID.
            return query.Where(p =>
                p.Id == id ||
                p.BillId == id ||
                (p.TransactionRef != null && p.TransactionRef.Contains(s)) ||

                // Order number (takeaway, older payments, or any round of a combined bill)
                (p.Order != null && p.Order.OrderNo.Contains(s)) ||
                (p.Bill != null && p.Bill.Orders.Any(o => o.OrderNo.Contains(s))) ||

                // Table number (dine-in)
                (p.Bill != null && p.Bill.DiningSession != null &&
                 p.Bill.DiningSession.Table.TableNumber.Contains(s)) ||
                (p.Order != null && p.Order.DiningSession != null &&
                 p.Order.DiningSession.Table.TableNumber.Contains(s)) ||

                // Customer name
                (p.Order != null && p.Order.User.FullName.Contains(s)) ||
                (p.Bill != null && p.Bill.DiningSession != null &&
                 p.Bill.DiningSession.Customer != null &&
                 p.Bill.DiningSession.Customer.FullName.Contains(s)) ||
                (p.Bill != null && p.Bill.Orders.Any(o => o.User.FullName.Contains(s))));
        }
    }
}
