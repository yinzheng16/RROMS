using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 1 (US50 Home page). Read-only: never changes other modules' data.
    public class HomeService : IHomeService
    {
        private readonly IRepository<MenuItem> _menuItems;
        private readonly IRepository<MenuCategory> _categories;
        private readonly IRepository<OrderItem> _orderItems;
        private readonly IRepository<Promotion> _promotions;
        private readonly IRepository<OperatingHour> _hours;
        private readonly IRepository<RestaurantSetting> _settings;
        private readonly IRepository<RestaurantTable> _tables;

        // Used until Members 2 and 5 have saved real hours / settings, so the page is never empty.
        private const string DefaultAddress = "12 Jalan Bukit Bintang, 55100 Kuala Lumpur";
        private const string DefaultPhone = "+60 3-1234 5678";

        // Last orders are taken this many minutes before closing (shown in the hero's "last" mood).
        // If the team later adds this to RestaurantSettings (Member 5), read it from there instead.
        private const int LastOrderMinutes = 30;

        // Quick booking box rules. Member 2's booking page should use the same numbers.
        // (If the team adds them to RestaurantSettings later, read them from there instead.)
        private const int BookingDaysAhead = 60;               // customers can book up to 60 days ahead
        private const int LastBookingMinutesBeforeClose = 60;  // last booking 1 hour before closing
        private const int DefaultMaxGuests = 20;               // used only if there are no tables yet

        public HomeService(
            IRepository<MenuItem> menuItems,
            IRepository<MenuCategory> categories,
            IRepository<OrderItem> orderItems,
            IRepository<Promotion> promotions,
            IRepository<OperatingHour> hours,
            IRepository<RestaurantSetting> settings,
            IRepository<RestaurantTable> tables)
        {
            _menuItems = menuItems;
            _categories = categories;
            _orderItems = orderItems;
            _promotions = promotions;
            _hours = hours;
            _settings = settings;
            _tables = tables;
        }

        // ---------- Signature menu (Member 3's menu) ----------

        public async Task<List<HomeDish>> GetSignatureDishesAsync(int count = 3)
        {
            // How many times each dish has been ordered (most popular first).
            var orderedCounts = await _orderItems.Query()
                .GroupBy(oi => oi.MenuItemId)
                .Select(g => new { MenuItemId = g.Key, Total = g.Sum(oi => oi.Quantity) })
                .ToDictionaryAsync(x => x.MenuItemId, x => x.Total);

            var items = await _menuItems.Query()
                .AsNoTracking()
                .Include(m => m.Category)
                .Where(m => m.IsAvailable)
                .ToListAsync();

            var discounts = await DiscountsAsync();

            var sorted = items
                .OrderByDescending(m => orderedCounts.GetValueOrDefault(m.Id))
                .ThenBy(m => m.Category.DisplayOrder)
                .ThenBy(m => m.Name)
                .ToList();

            // Variety: first the best dish from different categories (not 3 small bites in a row),
            // then fill up with the next best if there are fewer categories than `count`.
            var top = new List<MenuItem>();
            var usedCategories = new HashSet<int>();
            foreach (var m in sorted)
            {
                if (top.Count == count) break;
                if (usedCategories.Add(m.CategoryId)) top.Add(m);
            }
            foreach (var m in sorted)
            {
                if (top.Count == count) break;
                if (!top.Contains(m)) top.Add(m);
            }

            // The single most ordered dish gets the "Popular" tag (only once there are real orders).
            var mostOrderedId = orderedCounts.Count > 0 ? orderedCounts.MaxBy(x => x.Value).Key : -1;

            return top.Select(m => new HomeDish(
                    m.Name,
                    m.Description,
                    m.Price,
                    discounts.TryGetValue(m.Id, out var pct) ? pct : null,
                    m.ImageUrl,
                    m.Category.Name,
                    m.Id == mostOrderedId,
                    m.Id))
                .ToList();
        }

        // ---------- Limited time only (Member 3's promotions; Member 4's announcements later) ----------

        public async Task<List<HomeOffer>> GetActiveOffersAsync(int count = 3)
        {
            var now = DateTime.Now;

            var promotions = await ActivePromotions(now)
                .AsNoTracking()
                .Include(p => p.MenuItem).ThenInclude(m => m.Category)
                .OrderBy(p => p.EndDate)                // ending soonest first
                .ThenByDescending(p => p.StartDate)     // then the newest
                .Take(count)
                .ToListAsync();

            // US39 (Member 4): when announcements exist, add the active ones here with the same rule,
            // then sort the combined list by EndDate and take `count` again.
            return promotions
                .Select(p => new HomeOffer(
                    p.Title,
                    p.MenuItem.Name,
                    p.MenuItem.Price,
                    p.DiscountPercent,
                    p.MenuItem.ImageUrl,
                    p.MenuItem.Category?.Name ?? "",
                    p.EndDate,
                    p.MenuItem.Id))
                .ToList();
        }

        // ---------- Omikuji box ----------

        public async Task<List<OmikujiDish>> GetOmikujiDishesAsync()
        {
            var items = await _menuItems.Query()
                .AsNoTracking()
                .Include(m => m.Category)
                .Where(m => m.IsAvailable)
                .OrderBy(m => m.Name)
                .Take(60)
                .ToListAsync();

            var discounts = await DiscountsAsync();

            return items.Select(m => new OmikujiDish(
                    m.Name,
                    m.Category.Name,
                    discounts.TryGetValue(m.Id, out var pct) ? m.Price * (100 - pct) / 100 : m.Price,
                    m.Id))
                .ToList();
        }

        // Active = ticked active, already started, and the end DATE is today or later
        // (so a promotion saved as "ends 25 Oct" still shows for the whole of 25 Oct).
        private IQueryable<Promotion> ActivePromotions(DateTime now)
        {
            var today = now.Date;
            return _promotions.Query().Where(p => p.IsActive && p.StartDate <= now && p.EndDate >= today);
        }

        // Biggest active discount for each dish.
        private Task<Dictionary<int, decimal>> DiscountsAsync() =>
            ActivePromotions(DateTime.Now)
                .GroupBy(p => p.MenuItemId)
                .Select(g => new { MenuItemId = g.Key, Percent = g.Max(p => p.DiscountPercent) })
                .ToDictionaryAsync(x => x.MenuItemId, x => x.Percent);

        // ---------- Opening hours (Member 2) ----------

        public async Task<List<HomeHoursRow>> GetOpeningHoursAsync()
        {
            var week = await LoadWeekAsync();
            var rows = new List<HomeHoursRow>();

            // Group days next to each other that have exactly the same hours.
            var i = 0;
            while (i < week.Count)
            {
                var j = i;
                while (j + 1 < week.Count && SameHours(week[j], week[j + 1])) j++;

                var days = i == j ? week[i].Day.ToString() : $"{week[i].Day} – {week[j].Day}";
                rows.Add(week[i].IsClosed
                    ? new HomeHoursRow(days, "Closed", true)
                    : new HomeHoursRow(days, $"{Time(week[i].OpenTime)} – {Time(week[i].CloseTime)}", false));
                i = j + 1;
            }
            return rows;
        }

        public async Task<BookingSetup> GetBookingSetupAsync()
        {
            var week = await LoadWeekAsync();
            var hours = week.ToDictionary(
                h => (int)h.Day,
                h => h.IsClosed ? null : new[] { (int)h.OpenTime.ToTimeSpan().TotalMinutes, (int)h.CloseTime.ToTimeSpan().TotalMinutes });

            // Biggest active table or room decides the guest limit (VVIP room = 20 today).
            var biggest = await _tables.Query().Where(t => t.IsActive).Select(t => (int?)t.Capacity).MaxAsync();

            return new BookingSetup(
                hours,
                biggest ?? DefaultMaxGuests,
                BookingDaysAhead,
                LastBookingMinutesBeforeClose,
                30,
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture));
        }

        // ---------- Address, phone, hero mood, numbers strip ----------

        public async Task<HomeInfo> GetInfoAsync()
        {
            var setting = await _settings.Query().AsNoTracking().FirstOrDefaultAsync();
            var week = await LoadWeekAsync();
            var openDays = week.Where(h => !h.IsClosed).ToList();
            var (mood, moodText) = WorkOutMood(week, DateTime.Now);

            // "Closing today" in the numbers strip: today's own closing time (each day can differ).
            var todayHours = week.First(h => h.Day == DateTime.Now.DayOfWeek);
            var lastOrders = todayHours.IsClosed ? "Rest day" : Time(todayHours.CloseTime);

            return new HomeInfo(
                string.IsNullOrWhiteSpace(setting?.Address) ? DefaultAddress : setting!.Address!,
                string.IsNullOrWhiteSpace(setting?.Phone) ? DefaultPhone : setting!.Phone!,
                mood,
                moodText,
                await _menuItems.Query().CountAsync(m => m.IsAvailable),
                await _categories.Query().CountAsync(),
                openDays.Count,
                lastOrders);
        }

        // before / open / last / closed, plus the text for the little status pill.
        private static (string Mood, string Text) WorkOutMood(List<OperatingHour> week, DateTime now)
        {
            OperatingHour For(DateTime d) => week.First(h => h.Day == d.DayOfWeek);

            // Start and end of one day's opening. Closing at or before the opening time means "after midnight".
            (DateTime Start, DateTime End) Window(OperatingHour h, DateTime date)
            {
                var start = date.Date + h.OpenTime.ToTimeSpan();
                var end = date.Date + h.CloseTime.ToTimeSpan();
                if (h.CloseTime <= h.OpenTime) end = end.AddDays(1);
                return (start, end);
            }

            // "last" mood starts 1 hour before closing; last orders are LastOrderMinutes before closing.
            (string, string) Inside(DateTime end, OperatingHour h) =>
                end - now <= TimeSpan.FromMinutes(LastOrderMinutes)
                    ? ("last", $"Last orders have closed · we close at {Time(h.CloseTime)}")
                : end - now <= TimeSpan.FromHours(1)
                    ? ("last", $"Last orders {Time(h.CloseTime.AddMinutes(-LastOrderMinutes))} · we close at {Time(h.CloseTime)}")
                    : ("open", $"We're open now · until {Time(h.CloseTime)}");

            // Still open from yesterday (late night after midnight)?
            var yesterday = For(now.AddDays(-1));
            if (!yesterday.IsClosed)
            {
                var y = Window(yesterday, now.AddDays(-1));
                if (now >= y.Start && now < y.End) return Inside(y.End, yesterday);
            }

            var today = For(now);
            if (!today.IsClosed)
            {
                var t = Window(today, now);
                if (now < t.Start)
                {
                    var wait = t.Start - now;
                    var inText = wait.Hours > 0 ? $"in {wait.Hours} h {wait.Minutes} min" : $"in {Math.Max(1, wait.Minutes)} min";
                    return ("before", $"Opens today at {Time(today.OpenTime)} · {inText}");
                }
                if (now < t.End) return Inside(t.End, today);
            }

            // Closed: find the next day we open.
            for (var add = 1; add <= 7; add++)
            {
                var next = For(now.AddDays(add));
                if (next.IsClosed) continue;
                var when = add == 1 ? "tomorrow" : next.Day.ToString();
                var lead = today.IsClosed ? "Closed today" : "Closed for tonight";
                return ("closed", $"{lead} · open {when} from {Time(next.OpenTime)}");
            }
            return ("closed", "Closed for now · see you soon");
        }

        // Monday first. If Member 2 hasn't saved hours yet: Tue–Sun 5 PM – midnight, closed Monday.
        private async Task<List<OperatingHour>> LoadWeekAsync()
        {
            var saved = await _hours.Query().AsNoTracking().ToListAsync();
            var week = new List<OperatingHour>();
            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                                        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
            {
                week.Add(saved.FirstOrDefault(h => h.Day == day) ?? new OperatingHour
                {
                    Day = day,
                    OpenTime = new TimeOnly(17, 0),
                    CloseTime = new TimeOnly(0, 0),
                    IsClosed = day == DayOfWeek.Monday
                });
            }
            return week;
        }

        private static bool SameHours(OperatingHour a, OperatingHour b) =>
            a.IsClosed == b.IsClosed && (a.IsClosed || (a.OpenTime == b.OpenTime && a.CloseTime == b.CloseTime));

        private static string Time(TimeOnly t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);   // 17:00 -> "5:00 PM", 00:00 -> "12:00 AM"
    }
}
