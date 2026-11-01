using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 5 (US44 Filtered reports).
    // Builds the reservations, orders, sales and menu-item reports for Pages/Admin/Reports.
    // Read-only: this service never changes any data.
    public class SalesReportService : ISalesReportService
    {
        private static readonly CultureInfo My = CultureInfo.GetCultureInfo("en-MY");

        private readonly IRepository<Reservation> _reservations;
        private readonly IRepository<Order> _orders;
        private readonly IRepository<Payment> _payments;
        private readonly IRepository<OrderItem> _orderItems;
        private readonly IRepository<MenuItem> _menuItems;
        private readonly IProfileService _profiles;   // Member 1: allergy / diet note (US03)

        public SalesReportService(
            IRepository<Reservation> reservations,
            IRepository<Order> orders,
            IRepository<Payment> payments,
            IRepository<OrderItem> orderItems,
            IRepository<MenuItem> menuItems,
            IProfileService profiles)
        {
            _reservations = reservations;
            _orders = orders;
            _payments = payments;
            _orderItems = orderItems;
            _menuItems = menuItems;
            _profiles = profiles;
        }

        // One record (reservation, order, payment or order line) in a common shape,
        // so filtering, grouping and the chart work the same way for every report.
        private sealed class Fact
        {
            public DateTime Date { get; init; }
            public string Customer { get; init; } = "Customer";
            public string UserId { get; init; } = string.Empty;
            public string Group { get; init; } = string.Empty;   // status or category
            public string? Kind { get; init; }                   // order type or payment method
            public decimal Amount { get; init; }                 // guests, RM or line value
            public int Count { get; init; }                      // items or portions
            public object Source { get; init; } = null!;
        }

        public async Task<ReportResult> BuildAsync(ReportQuery q)
        {
            var from = q.From.Date;
            var to = q.To.Date;
            var prevFrom = q.PreviousFrom;
            var prevTo = q.PreviousTo;
            var shift = (from - prevFrom).Days;   // chart: day N of this period vs day N of the earlier one

            // One trip to the database covers this period and the one before it.
            var facts = await LoadFactsAsync(q.Type, prevFrom, to.AddDays(1));

            var current = Filter(facts, q, from, to, useDay: true, useGroups: true, useCustomer: true);
            var previous = Filter(facts, q, prevFrom, prevTo, useDay: false, useGroups: true, useCustomer: true);
            var forChart = Filter(facts, q, from, to, useDay: false, useGroups: true, useCustomer: true);

            var result = new ReportResult
            {
                Query = q,
                Title = TitleOf(q.Type),
                MainKpi = q.Type == ReportType.Orders ? 1 : q.Type == ReportType.Reservations ? 1 : 0,
                ChartLabel = q.Type switch
                {
                    ReportType.Reservations => "Guests per day",
                    ReportType.Orders => "Order value per day",
                    ReportType.Sales => "Net sales per day",
                    _ => "Portions sold per day"
                },
                GroupLabel = q.Type == ReportType.Menu ? "Category" : "Status",
                TopLabel = q.Type switch
                {
                    ReportType.Reservations => "Top guests (people)",
                    ReportType.Menu => "Top guests (portions)",
                    _ => "Top customers (RM)"
                },
                KindLabel = q.Type == ReportType.Orders ? "Type" : q.Type == ReportType.Sales ? "Payment method" : string.Empty,
                RowNoun = q.Type switch
                {
                    ReportType.Reservations => "reservations",
                    ReportType.Orders => "orders",
                    ReportType.Sales => "payments",
                    _ => "menu items"
                }
            };

            // ---------- Numbers at the top ----------
            var now = Kpis(q.Type, current);
            var before = Kpis(q.Type, previous);
            for (var i = 0; i < now.Count; i++)
            {
                if (q.Compare && q.Day == null && !now[i].IsText && before[i].Value != 0)
                {
                    now[i].Change = (int)Math.Round((now[i].Value - before[i].Value) * 100 / before[i].Value);
                }
                result.Kpis.Add(now[i]);
            }
            result.Lead = Lead(q, result);

            // ---------- Chart: one bar per day ----------
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var value = ChartValue(q.Type, forChart.Where(f => f.Date.Date == day));
                decimal? prev = null;
                if (q.Compare)
                {
                    var prevDay = day.AddDays(-shift);
                    prev = ChartValue(q.Type, previous.Where(f => f.Date.Date == prevDay));
                }
                result.Chart.Add(new ReportChartPoint
                {
                    Day = day,
                    Value = value,
                    Previous = prev,
                    Display = ChartText(q.Type, value),
                    PreviousDisplay = prev.HasValue ? ChartText(q.Type, prev.Value) : null
                });
            }

            // ---------- Status / category buttons (counted without that filter) ----------
            var forGroups = Filter(facts, q, from, to, useDay: true, useGroups: false, useCustomer: true);
            var groups = forGroups
                .GroupBy(f => f.Group)
                .Select(g => new { Name = g.Key, Value = q.Type == ReportType.Menu ? g.Sum(f => f.Amount) : (decimal)g.Count() })
                .OrderByDescending(g => g.Value)
                .ToList();
            // Keep a chosen status visible even when nothing matches it any more.
            foreach (var chosen in q.Groups.Where(c => groups.All(g => g.Name != c)))
            {
                groups.Add(new { Name = chosen, Value = 0m });
            }
            var groupTotal = groups.Sum(g => g.Value);
            foreach (var g in groups)
            {
                result.Groups.Add(new ReportGroup
                {
                    Name = g.Name,
                    Value = g.Value,
                    Display = q.Type == ReportType.Menu
                        ? (groupTotal == 0 ? "0%" : Math.Round(g.Value * 100 / groupTotal) + "%")
                        : g.Value.ToString("0"),
                    Color = ColorOf(g.Name),
                    Selected = q.Groups.Contains(g.Name)
                });
            }

            // ---------- Top customers (counted without the customer filter) ----------
            var forTop = Filter(facts, q, from, to, useDay: true, useGroups: true, useCustomer: false);
            foreach (var c in forTop
                .Where(f => q.Type != ReportType.Sales || f.Group == Label(PaymentStatus.Paid))
                .GroupBy(f => f.Customer)
                .Select(g => new { g.Key, Value = q.Type == ReportType.Menu ? g.Sum(f => f.Count) : g.Sum(f => f.Amount) })
                .OrderByDescending(c => c.Value)
                .Take(5))
            {
                var text = q.Type is ReportType.Orders or ReportType.Sales ? c.Value.ToString("N2", My) : c.Value.ToString("0");
                result.TopCustomers.Add(new KeyValuePair<string, string>(c.Key, text));
            }

            result.CustomerNames.AddRange(Filter(facts, q, from, to, false, false, false)
                .Select(f => f.Customer).Distinct().OrderBy(n => n));

            if (q.Type == ReportType.Orders)
            {
                result.KindOptions.AddRange(Enum.GetValues<OrderType>().Select(t => Label(t)));
            }
            else if (q.Type == ReportType.Sales)
            {
                result.KindOptions.AddRange(Enum.GetValues<PaymentMethod>().Select(m => Label(m)));
            }

            // ---------- Table ----------
            if (q.Type == ReportType.Menu)
            {
                await BuildMenuRowsAsync(result, q, current);
            }
            else
            {
                BuildRows(result, q, current);
            }
            Sort(result, q);
            await AddDiningNotesAsync(result);

            return result;
        }

        // =====================================================================
        // Loading
        // =====================================================================

        private async Task<List<Fact>> LoadFactsAsync(ReportType type, DateTime start, DateTime endExclusive)
        {
            switch (type)
            {
                case ReportType.Reservations:
                    var reservations = await _reservations.Query().AsNoTracking()
                        .Include(r => r.User)
                        .Include(r => r.Table)
                        .Where(r => r.ReservedAt >= start && r.ReservedAt < endExclusive)
                        .ToListAsync();
                    return reservations.Select(r => new Fact
                    {
                        Date = r.ReservedAt,
                        Customer = NameOf(r.User),
                        UserId = r.UserId,
                        Group = Label(r.Status),
                        Amount = r.Guests,
                        Source = r
                    }).ToList();

                case ReportType.Orders:
                    var orders = await _orders.Query().AsNoTracking()
                        .Include(o => o.User)
                        .Include(o => o.Reservation)
                        .Include(o => o.Items).ThenInclude(i => i.MenuItem)
                        .Where(o => o.CreatedAt >= start && o.CreatedAt < endExclusive)
                        .ToListAsync();
                    return orders.Select(o => new Fact
                    {
                        Date = o.CreatedAt,
                        Customer = NameOf(o.User),
                        UserId = o.UserId,
                        Group = Label(o.Status),
                        Kind = Label(o.OrderType),
                        Amount = o.TotalAmount,
                        Count = o.Items.Sum(i => i.Quantity),
                        Source = o
                    }).ToList();

                case ReportType.Sales:
                    var payments = await _payments.Query().AsNoTracking()
                        // Older payments point to one Order; newer ones (US32 combined billing) point to a Bill.
                        .Include(p => p.Order).ThenInclude(o => o!.User)
                        .Include(p => p.Order).ThenInclude(o => o!.Items).ThenInclude(i => i.MenuItem)
                        .Include(p => p.Bill).ThenInclude(b => b!.Orders).ThenInclude(o => o.User)
                        .Include(p => p.Bill).ThenInclude(b => b!.Orders).ThenInclude(o => o.Items).ThenInclude(i => i.MenuItem)
                        .Where(p => (p.PaidAt ?? p.CreatedAt) >= start && (p.PaidAt ?? p.CreatedAt) < endExclusive)
                        .AsSplitQuery()
                        .ToListAsync();
                    return payments.Select(p =>
                    {
                        var first = OrdersOf(p).FirstOrDefault();
                        return new Fact
                        {
                            Date = p.PaidAt ?? p.CreatedAt,
                            Customer = NameOf(first?.User),
                            UserId = first?.UserId ?? string.Empty,
                            Group = Label(p.Status),
                            Kind = Label(p.Method),
                            Amount = p.Amount,
                            Source = p
                        };
                    }).ToList();

                default:
                    // Menu: every line of every order that was not cancelled.
                    var lines = await _orderItems.Query().AsNoTracking()
                        .Include(i => i.Order).ThenInclude(o => o.User)
                        .Include(i => i.MenuItem).ThenInclude(m => m.Category)
                        .Where(i => i.Order.CreatedAt >= start && i.Order.CreatedAt < endExclusive
                                    && i.Order.Status != OrderStatus.Cancelled)
                        .ToListAsync();
                    return lines.Select(i => new Fact
                    {
                        Date = i.Order.CreatedAt,
                        Customer = NameOf(i.Order.User),
                        UserId = i.Order.UserId,
                        Group = i.MenuItem.Category?.Name ?? "Other",
                        Amount = i.Quantity * i.UnitPrice,
                        Count = i.Quantity,
                        Source = i
                    }).ToList();
            }
        }

        private static List<Fact> Filter(List<Fact> facts, ReportQuery q, DateTime from, DateTime to,
            bool useDay, bool useGroups, bool useCustomer)
        {
            var end = to.Date.AddDays(1);
            IEnumerable<Fact> rs = facts.Where(f => f.Date >= from.Date && f.Date < end);

            if (useDay && q.Day.HasValue)
            {
                rs = rs.Where(f => f.Date.Date == q.Day.Value.Date);
            }
            if (useCustomer && !string.IsNullOrWhiteSpace(q.Customer))
            {
                var term = q.Customer.Trim();
                rs = rs.Where(f => f.Customer.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrEmpty(q.Kind))
            {
                rs = rs.Where(f => f.Kind == q.Kind);
            }
            if (useGroups && q.Groups.Count > 0)
            {
                rs = rs.Where(f => q.Groups.Contains(f.Group));
            }
            return rs.ToList();
        }

        // =====================================================================
        // Numbers
        // =====================================================================

        private static List<ReportKpi> Kpis(ReportType type, List<Fact> fs)
        {
            switch (type)
            {
                case ReportType.Reservations:
                    var n = fs.Count;
                    var guests = fs.Sum(f => f.Amount);
                    var noShow = n == 0 ? 0 : fs.Count(f => f.Group == Label(ReservationStatus.NoShow)) * 100m / n;
                    return new List<ReportKpi>
                    {
                        new() { Label = "Reservations", Value = n, Display = n.ToString() },
                        new() { Label = "Guests", Value = guests, Display = guests.ToString("0") },
                        new() { Label = "Average party", Value = n == 0 ? 0 : guests / n, Display = (n == 0 ? 0 : guests / n).ToString("0.0") },
                        new() { Label = "No-show rate", Value = noShow, Display = noShow.ToString("0") + "%", GoodWhenDown = true }
                    };

                case ReportType.Orders:
                    var orders = fs.Count;
                    var value = fs.Sum(f => f.Amount);
                    return new List<ReportKpi>
                    {
                        new() { Label = "Orders", Value = orders, Display = orders.ToString() },
                        new() { Label = "Order value", Value = value, Display = Rm(value) },
                        new() { Label = "Average order", Value = orders == 0 ? 0 : value / orders, Display = Rm(orders == 0 ? 0 : value / orders) },
                        new() { Label = "Items ordered", Value = fs.Sum(f => f.Count), Display = fs.Sum(f => f.Count).ToString() }
                    };

                case ReportType.Sales:
                    var paid = fs.Where(f => f.Group == Label(PaymentStatus.Paid)).Sum(f => f.Amount);
                    var refunded = fs.Where(f => f.Group == Label(PaymentStatus.Refunded)).Sum(f => f.Amount);
                    return new List<ReportKpi>
                    {
                        new() { Label = "Net sales", Value = paid - refunded, Display = Rm(paid - refunded) },
                        new() { Label = "Paid", Value = paid, Display = Rm(paid) },
                        new() { Label = "Refunded", Value = refunded, Display = Rm(refunded), GoodWhenDown = true },
                        new() { Label = "Payments", Value = fs.Count, Display = fs.Count.ToString() }
                    };

                default:
                    var best = fs.GroupBy(f => ((OrderItem)f.Source).MenuItem.Name)
                        .Select(g => new { g.Key, Sold = g.Sum(f => f.Count) })
                        .OrderByDescending(g => g.Sold)
                        .FirstOrDefault();
                    var portions = fs.Sum(f => f.Count);
                    var revenue = fs.Sum(f => f.Amount);
                    var kinds = fs.Select(f => ((OrderItem)f.Source).MenuItemId).Distinct().Count();
                    return new List<ReportKpi>
                    {
                        new() { Label = "Portions sold", Value = portions, Display = portions.ToString() },
                        new() { Label = "Menu revenue", Value = revenue, Display = Rm(revenue) },
                        new() { Label = "Dishes ordered", Value = kinds, Display = kinds + (kinds == 1 ? " dish" : " dishes") },
                        new() { Label = "Best seller", Display = best?.Key ?? "None yet", IsText = true }
                    };
            }
        }

        private static decimal ChartValue(ReportType type, IEnumerable<Fact> fs) => type switch
        {
            ReportType.Sales => fs.Where(f => f.Group == Label(PaymentStatus.Paid)).Sum(f => f.Amount)
                              - fs.Where(f => f.Group == Label(PaymentStatus.Refunded)).Sum(f => f.Amount),
            ReportType.Menu => fs.Sum(f => f.Count),
            _ => fs.Sum(f => f.Amount)
        };

        private static string ChartText(ReportType type, decimal v) => type switch
        {
            ReportType.Reservations => v.ToString("0") + (v == 1 ? " guest" : " guests"),
            ReportType.Menu => v.ToString("0") + (v == 1 ? " portion" : " portions"),
            _ => Rm(v)
        };

        private static string Lead(ReportQuery q, ReportResult r)
        {
            var k = r.Kpis;
            var main = k[r.MainKpi];
            var compare = "";
            if (main.Change.HasValue)
            {
                var span = string.IsNullOrEmpty(q.CompareSpan)
                    ? (q.Days == 1 ? "the day before" : $"the {q.Days} days before")
                    : q.CompareSpan;
                compare = main.Change == 0
                    ? $", the same as {span}"
                    : $", {Math.Abs(main.Change.Value)}% {(main.Change > 0 ? "more" : "less")} than {span}";
            }

            return q.Type switch
            {
                ReportType.Reservations => $"{k[0].Display} reservations for {k[1].Display} guests{compare}.",
                ReportType.Orders => $"{k[0].Display} orders worth {k[1].Display}{compare}.",
                ReportType.Sales => $"{k[0].Display} in net sales from {k[3].Display} payments{compare}.",
                _ => k[0].Value == 0
                    ? "No dishes sold in this period yet."
                    : $"{k[0].Display} portions sold, led by {k[3].Display}{compare}."
            };
        }

        // =====================================================================
        // Table rows
        // =====================================================================

        private static void BuildRows(ReportResult result, ReportQuery q, List<Fact> fs)
        {
            switch (q.Type)
            {
                case ReportType.Reservations:
                    AddColumns(result, ("Reference", false), ("Date", false), ("Time", false), ("Customer", false),
                        ("Guests", true), ("Table", false), ("Status", false));
                    foreach (var f in fs)
                    {
                        var r = (Reservation)f.Source;
                        var row = new ReportRow { Key = "r" + r.Id, UserId = r.UserId };
                        row.Cells.Add(new ReportCell { Text = r.ReferenceNo, Sort = r.ReferenceNo });
                        row.Cells.Add(new ReportCell { Text = r.ReservedAt.ToString("d MMM yyyy"), Sort = r.ReservedAt });
                        row.Cells.Add(new ReportCell { Text = r.ReservedAt.ToString("HH:mm"), Sort = r.ReservedAt.TimeOfDay });
                        row.Cells.Add(new ReportCell { Text = f.Customer, Sort = f.Customer });
                        row.Cells.Add(new ReportCell { Text = r.Guests.ToString(), Sort = r.Guests });
                        row.Cells.Add(new ReportCell { Text = r.Table?.TableNumber ?? "—", Sort = r.Table?.TableNumber ?? "" });
                        row.Cells.Add(new ReportCell { Text = f.Group, Badge = BadgeOf(f.Group), Sort = f.Group });
                        row.Details.Add(Pair("Special request", string.IsNullOrWhiteSpace(r.SpecialRequest) ? "None" : r.SpecialRequest));
                        row.Details.Add(Pair("Booked on", r.CreatedAt.ToString("d MMM yyyy, HH:mm")));
                        row.Details.Add(Pair("Length", r.DurationMinutes + " minutes"));
                        result.Rows.Add(row);
                    }
                    result.Total = TotalRow("Total", "", "", $"{fs.Count} reservations",
                        fs.Sum(f => f.Amount).ToString("0"), "", "");
                    break;

                case ReportType.Orders:
                    AddColumns(result, ("Order", false), ("Date", false), ("Customer", false), ("Type", false),
                        ("Items", true), ("Status", false), ("Total (RM)", true));
                    foreach (var f in fs)
                    {
                        var o = (Order)f.Source;
                        var row = new ReportRow { Key = "o" + o.Id, UserId = o.UserId };
                        row.Cells.Add(new ReportCell { Text = o.OrderNo, Sort = o.OrderNo });
                        row.Cells.Add(new ReportCell { Text = o.CreatedAt.ToString("d MMM yyyy"), Sort = o.CreatedAt });
                        row.Cells.Add(new ReportCell { Text = f.Customer, Sort = f.Customer });
                        row.Cells.Add(new ReportCell { Text = f.Kind ?? "", Sort = f.Kind ?? "" });
                        row.Cells.Add(new ReportCell { Text = f.Count.ToString(), Sort = f.Count });
                        row.Cells.Add(new ReportCell { Text = f.Group, Badge = BadgeOf(f.Group), Sort = f.Group });
                        row.Cells.Add(new ReportCell { Text = o.TotalAmount.ToString("N2", My), Sort = o.TotalAmount });
                        AddLines(row, o.Items);
                        if (o.Reservation != null)
                        {
                            row.Details.Add(Pair("Reservation", o.Reservation.ReferenceNo));
                        }
                        if (!string.IsNullOrWhiteSpace(o.Notes))
                        {
                            row.Details.Add(Pair("Order notes", o.Notes));
                        }
                        result.Rows.Add(row);
                    }
                    result.Total = TotalRow("Total", "", $"{fs.Count} orders", "",
                        fs.Sum(f => f.Count).ToString(), "", fs.Sum(f => f.Amount).ToString("N2", My));
                    break;

                case ReportType.Sales:
                    AddColumns(result, ("Payment", false), ("Date", false), ("Order", false), ("Customer", false),
                        ("Method", false), ("Status", false), ("Amount (RM)", true));
                    foreach (var f in fs)
                    {
                        var p = (Payment)f.Source;
                        var row = new ReportRow { Key = "p" + p.Id };
                        var no = "PAY-" + p.Id.ToString("D5");
                        row.Cells.Add(new ReportCell { Text = no, Sort = p.Id });
                        row.Cells.Add(new ReportCell { Text = f.Date.ToString("d MMM yyyy"), Sort = f.Date });
                        var orderNo = OrderNoOf(p);
                        row.Cells.Add(new ReportCell { Text = orderNo, Sort = orderNo });
                        row.Cells.Add(new ReportCell { Text = f.Customer, Sort = f.Customer });
                        row.Cells.Add(new ReportCell { Text = f.Kind ?? "", Sort = f.Kind ?? "" });
                        row.Cells.Add(new ReportCell { Text = f.Group, Badge = BadgeOf(f.Group), Sort = f.Group });
                        row.Cells.Add(new ReportCell { Text = p.Amount.ToString("N2", My), Sort = p.Amount });
                        row.Details.Add(Pair("Paid at", p.PaidAt?.ToString("d MMM yyyy, HH:mm") ?? "Not paid yet"));
                        if (!string.IsNullOrWhiteSpace(p.TransactionRef))
                        {
                            row.Details.Add(Pair("Transaction ref", p.TransactionRef));
                        }
                        if (p.Order == null && p.Bill != null && p.Bill.Orders.Count > 0)
                        {
                            row.Details.Add(Pair("Orders on this bill", string.Join(", ", p.Bill.Orders.Select(o => o.OrderNo))));
                        }
                        AddLines(row, OrdersOf(p).SelectMany(o => o.Items));
                        result.Rows.Add(row);
                    }
                    result.Total = TotalRow("Total", "", "", $"{fs.Count} payments", "", "",
                        fs.Sum(f => f.Amount).ToString("N2", My));
                    break;
            }
        }

        private async Task BuildMenuRowsAsync(ReportResult result, ReportQuery q, List<Fact> fs)
        {
            AddColumns(result, ("Item", false), ("Category", false), ("Price (RM)", true), ("Sold", true),
                ("Revenue (RM)", true), ("Share", true));

            // Show every dish (also the ones nobody ordered) unless the admin is looking at
            // one customer or one day, where empty rows would only be noise.
            var showUnsold = string.IsNullOrWhiteSpace(q.Customer) && q.Day == null;
            var menu = await _menuItems.Query().AsNoTracking().Include(m => m.Category).ToListAsync();

            var sold = fs.GroupBy(f => ((OrderItem)f.Source).MenuItemId)
                .ToDictionary(g => g.Key, g => new
                {
                    Qty = g.Sum(f => f.Count),
                    Revenue = g.Sum(f => f.Amount),
                    Orders = g.Select(f => ((OrderItem)f.Source).OrderId).Distinct().Count()
                });
            var totalRevenue = fs.Sum(f => f.Amount);

            foreach (var m in menu
                .Where(m => q.Groups.Count == 0 || q.Groups.Contains(m.Category?.Name ?? "Other"))
                .OrderBy(m => m.Category?.DisplayOrder ?? 999).ThenBy(m => m.Name))
            {
                sold.TryGetValue(m.Id, out var s);
                if (s == null && !showUnsold)
                {
                    continue;
                }
                var qty = s?.Qty ?? 0;
                var revenue = s?.Revenue ?? 0;
                var share = totalRevenue == 0 ? 0 : (double)(revenue * 100 / totalRevenue);
                var category = m.Category?.Name ?? "Other";

                var row = new ReportRow { Key = "m" + m.Id };
                row.Cells.Add(new ReportCell { Text = m.Name, Sort = m.Name });
                row.Cells.Add(new ReportCell { Text = category, Badge = "rp-b-cat", Sort = category });
                row.Cells.Add(new ReportCell { Text = m.Price.ToString("N2", My), Sort = m.Price });
                row.Cells.Add(new ReportCell { Text = qty.ToString(), Sort = qty });
                row.Cells.Add(new ReportCell { Text = revenue.ToString("N2", My), Sort = revenue });
                row.Cells.Add(new ReportCell { Text = Math.Round(share) + "%", Share = share, Sort = share });
                row.Details.Add(Pair("Code", m.Code));
                row.Details.Add(Pair("Orders with this dish", (s?.Orders ?? 0).ToString()));
                row.Details.Add(Pair("Average per order", s == null || s.Orders == 0 ? "0" : ((double)s.Qty / s.Orders).ToString("0.0")));
                row.Details.Add(Pair("On the menu now", m.IsAvailable ? "Yes" : "No (unavailable)"));
                result.Rows.Add(row);
            }

            result.Total = TotalRow("Total", $"{result.Rows.Count} dishes", "",
                fs.Sum(f => f.Count).ToString(), totalRevenue.ToString("N2", My), totalRevenue == 0 ? "" : "100%");
        }

        private static void Sort(ReportResult result, ReportQuery q)
        {
            IEnumerable<ReportRow> rows = result.Rows;
            var col = q.SortColumn;
            if (col.HasValue && col.Value >= 0 && col.Value < result.Columns.Count)
            {
                rows = q.SortDescending
                    ? rows.OrderByDescending(r => r.Cells[col.Value].Sort)
                    : rows.OrderBy(r => r.Cells[col.Value].Sort);
            }
            else if (q.Type == ReportType.Menu)
            {
                // Best sellers first.
                rows = rows.OrderByDescending(r => r.Cells[3].Sort);
            }
            else
            {
                // Newest first.
                rows = rows.OrderByDescending(r => r.Cells[1].Sort);
                if (q.Type == ReportType.Reservations)
                {
                    rows = ((IOrderedEnumerable<ReportRow>)rows).ThenByDescending(r => r.Cells[2].Sort);
                }
            }
            var sorted = rows.ToList();
            result.Rows.Clear();
            result.Rows.AddRange(sorted);
        }

        // Allergy / diet note from the customer's profile, so staff and admin see it in context.
        private async Task AddDiningNotesAsync(ReportResult result)
        {
            var notes = new Dictionary<string, string?>();
            foreach (var row in result.Rows.Where(r => !string.IsNullOrEmpty(r.UserId)))
            {
                if (!notes.TryGetValue(row.UserId!, out var note))
                {
                    note = await _profiles.GetDiningSummaryAsync(row.UserId!);
                    notes[row.UserId!] = note;
                }
                row.Warning = note;
            }
        }

        // =====================================================================
        // Small helpers
        // =====================================================================

        private static void AddColumns(ReportResult r, params (string Title, bool Right)[] cols)
        {
            foreach (var c in cols)
            {
                r.Columns.Add(new ReportColumn { Title = c.Title, Right = c.Right });
            }
        }

        private static void AddLines(ReportRow row, IEnumerable<OrderItem> items)
        {
            foreach (var i in items)
            {
                row.Details.Add(Pair($"{i.Quantity} × {i.MenuItem?.Name ?? "Item"}", (i.Quantity * i.UnitPrice).ToString("N2", My)));
            }
        }

        private static ReportRow TotalRow(params string[] cells)
        {
            var row = new ReportRow { Key = "total" };
            foreach (var c in cells)
            {
                row.Cells.Add(new ReportCell { Text = c });
            }
            return row;
        }

        private static KeyValuePair<string, string> Pair(string k, string v) => new(k, v);

        private static string Rm(decimal v) => "RM " + v.ToString("N2", My);

        // The orders a payment covers: its own Order (older payments) or every order on its Bill (US32).
        private static IEnumerable<Order> OrdersOf(Payment p) =>
            p.Order != null ? new[] { p.Order } : p.Bill?.Orders ?? Enumerable.Empty<Order>();

        // What the "Order" column shows: the order number, the bill number, or a dash if neither is linked.
        private static string OrderNoOf(Payment p) =>
            p.Order != null ? p.Order.OrderNo
            : p.BillId.HasValue ? "BILL-" + p.BillId.Value.ToString("D5")
            : "—";

        private static string NameOf(ApplicationUser? u) =>
            string.IsNullOrWhiteSpace(u?.FullName) ? "Customer" : u!.FullName!;

        private static string TitleOf(ReportType t) => t switch
        {
            ReportType.Reservations => "Reservations",
            ReportType.Orders => "Customer orders",
            ReportType.Sales => "Sales transactions",
            _ => "Menu items"
        };

        // Uses the [Display(Name = "...")] text when there is one, e.g. NoShow → "No-show".
        public static string Label(Enum value) =>
            value.GetType().GetMember(value.ToString()).FirstOrDefault()?
                .GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();

        private static string BadgeOf(string status) => status switch
        {
            "Completed" or "Paid" => "rp-b-ok",
            "Pending" or "Preparing" or "Ready to Serve" => "rp-b-wait",
            "Cancelled" or "Refunded" or "Failed" or "No-show" => "rp-b-bad",
            _ => "rp-b-info"
        };

        private static string ColorOf(string name) => name switch
        {
            "Completed" or "Paid" => "#3F7A4C",
            "Confirmed" or "Seated" or "Served" => "#3D5A80",
            "Pending" or "Preparing" => "#A8741A",
            "Ready to Serve" => "#8C6A2E",
            "Cancelled" or "Refunded" or "Failed" => "#C9392B",
            "No-show" => "#7E2318",
            _ => CategoryColors[name.Sum(c => (int)c) % CategoryColors.Length]
        };

        private static readonly string[] CategoryColors =
            { "#1B1918", "#C9392B", "#3D5A80", "#3F7A4C", "#A8741A", "#B79B72", "#7E2318", "#5E5650" };
    }
}