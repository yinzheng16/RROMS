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
        private readonly IRepository<SpecialDate> _specials;
        private readonly IReservationService _reservations;

        // Used until Members 2 and 5 have saved real hours / settings, so the page is never empty.
        private const string DefaultAddress = "12 Jalan Bukit Bintang, 55100 Kuala Lumpur";
        private const string DefaultPhone = "+60 3-2142 6688";

        // Last orders: the team rule from Member 2 (30 minutes before closing or before a break).
        private const int LastOrderMinutes = BookingTimeRules.LastOrderMinutes;

        // Upcoming special dates (holidays, special hours) listed under the weekly hours.
        private const int SpecialDaysAhead = 14;
        private const int MaxSpecialRows = 3;

        public HomeService(
            IRepository<MenuItem> menuItems,
            IRepository<MenuCategory> categories,
            IRepository<OrderItem> orderItems,
            IRepository<Promotion> promotions,
            IRepository<OperatingHour> hours,
            IRepository<RestaurantSetting> settings,
            IRepository<SpecialDate> specials,
            IReservationService reservations)
        {
            _menuItems = menuItems;
            _categories = categories;
            _orderItems = orderItems;
            _promotions = promotions;
            _hours = hours;
            _settings = settings;
            _specials = specials;
            _reservations = reservations;
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

            var running = await ActivePromotions(now)
                .AsNoTracking()
                .Include(p => p.MenuItem).ThenInclude(m => m.Category)
                .ToListAsync();

            // One card per dish (its best deal), ending soonest first, then the biggest saving, then the newest.
            var promotions = running
                .GroupBy(p => p.MenuItemId)
                .Select(g => g.OrderByDescending(p => PromotionRules.EffectivePercent(p.MenuItem.Price, p.DiscountPercent, p.PromoPrice)).First())
                .OrderBy(p => p.EndDate.Date)
                .ThenByDescending(p => PromotionRules.EffectivePercent(p.MenuItem.Price, p.DiscountPercent, p.PromoPrice))
                .ThenByDescending(p => p.StartDate)
                .Take(count)
                .ToList();

            // US39 (Member 4): when announcements exist, add the active ones here with the same rule,
            // then sort the combined list by EndDate and take `count` again.
            return promotions
                .Select(p => new HomeOffer(
                    p.Title,
                    p.MenuItem.Name,
                    p.MenuItem.Price,
                    PromotionRules.EffectivePercent(p.MenuItem.Price, p.DiscountPercent, p.PromoPrice),
                    p.MenuItem.ImageUrl,
                    p.MenuItem.Category?.Name ?? "",
                    p.EndDate,
                    p.MenuItem.Id,
                    PromotionRules.PriceUnder(p.MenuItem.Price, p.DiscountPercent, p.PromoPrice)))   // exact promo price (US29)
                .ToList();
        }

        public Task<int> CountOfferDishesAsync() =>
            ActivePromotions(DateTime.Now).Select(p => p.MenuItemId).Distinct().CountAsync();

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
                    discounts.TryGetValue(m.Id, out var pct) ? OrderPricingRules.DiscountedPrice(m.Price, pct) : m.Price,
                    m.Id))
                .ToList();
        }

        // Active = ticked active, already started, and the end DATE is today or later
        // (so a promotion saved as "ends 25 Oct" still shows for the whole of 25 Oct).
        private IQueryable<Promotion> ActivePromotions(DateTime now)
            => PromotionRules.Running(_promotions.Query(), now);   // Member 3 / US29: one shared rule

        // Biggest active discount for each dish.
        // US29: fixed-price promotions count too (as the matching % of today's price).
        private async Task<Dictionary<int, decimal>> DiscountsAsync() =>
            PromotionRules.BestPercents(await ActivePromotions(DateTime.Now)
                .Select(p => new PromotionPriceRow(p.MenuItemId, p.MenuItem.Price, p.DiscountPercent, p.PromoPrice))
                .ToListAsync());

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
                    : new HomeHoursRow(days, HoursText(week[i]), false));
                i = j + 1;
            }

            // Then the next few special dates (holiday closures, special hours), e.g. "Thu 24 Dec · Christmas Eve".
            var today = DateTime.Today;
            var specials = (await LoadSpecialsAsync())
                .Where(x => (x.EndDate ?? x.Date).Date >= today && x.Date.Date <= today.AddDays(SpecialDaysAhead))
                .OrderBy(x => x.Date)
                .Take(MaxSpecialRows);

            foreach (var sd in specials)
            {
                var start = sd.Date.Date;
                var end = (sd.EndDate ?? sd.Date).Date;
                var when = start == end
                    ? start.ToString("ddd d MMM", CultureInfo.InvariantCulture)
                    : $"{start.ToString("d MMM", CultureInfo.InvariantCulture)} – {end.ToString("d MMM", CultureInfo.InvariantCulture)}";
                var closed = sd.IsClosed || !sd.OpenTime.HasValue || !sd.CloseTime.HasValue;

                rows.Add(new HomeHoursRow(
                    $"{when} · {sd.Name}",
                    closed ? "Closed" : $"{Time(TimeOnly.FromTimeSpan(sd.OpenTime!.Value))} – {Time(TimeOnly.FromTimeSpan(sd.CloseTime!.Value))}",
                    closed));
            }
            return rows;
        }

        // "10:00 AM – 3:00 PM · 5:00 PM – 10:00 PM" when the day has a break, otherwise "10:00 AM – 10:00 PM".
        private static string HoursText(OperatingHour h) =>
            h.BreakStart.HasValue && h.BreakEnd.HasValue
                ? $"{Time(h.OpenTime)} – {Time(h.BreakStart.Value)} · {Time(h.BreakEnd.Value)} – {Time(h.CloseTime)}"
                : $"{Time(h.OpenTime)} – {Time(h.CloseTime)}";

        // The quick booking box uses Member 2's booking rules (US12), so it never offers a time her page refuses:
        // bookable dates and the guest limit come from IReservationService, and the times for a date
        // are loaded from Index?handler=Slots (also IReservationService.GetTimeSlotsAsync).
        public async Task<BookingSetup> GetBookingSetupAsync()
        {
            var week = await LoadWeekAsync();
            var weeklyClosed = week.Where(h => h.IsClosed).Select(h => (int)h.Day).ToList();

            // Closed special dates in the booking range, so the calendar can say "Shut" instead of "Full".
            var today = DateTime.Today;
            var last = today.AddDays(BookingRules.MaxAdvanceDays);
            var closedDates = new List<string>();
            foreach (var sd in (await LoadSpecialsAsync()).Where(x => x.IsClosed || !x.OpenTime.HasValue || !x.CloseTime.HasValue))
            {
                for (var d = sd.Date.Date; d <= (sd.EndDate ?? sd.Date).Date; d = d.AddDays(1))
                {
                    if (d >= today && d <= last) closedDates.Add(IsoDate(d));
                }
            }

            return new BookingSetup(
                await _reservations.GetBookableDatesAsync(),
                closedDates,
                weeklyClosed,
                await _reservations.GetMaxPartySizeAsync(),
                BookingRules.MaxAdvanceDays,
                BookingTimeRules.ExactStepMinutes,
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture));
        }

        // ---------- Address, phone, hero mood, numbers strip ----------

        public async Task<HomeInfo> GetInfoAsync()
        {
            var setting = await _settings.Query().AsNoTracking().FirstOrDefaultAsync();
            var week = await LoadWeekAsync();
            var specials = await LoadSpecialsAsync();
            var now = DateTime.Now;
            var openDays = week.Where(h => !h.IsClosed).ToList();
            var (mood, moodText) = WorkOutMood(week, specials, now);

            // "Closing today" in the numbers strip: today's own closing time (special dates included).
            var todaySession = SessionFor(now.Date, week, specials);
            var lastOrders = todaySession.Closed ? "Rest day" : Time(todaySession.Close);

            return new HomeInfo(
                string.IsNullOrWhiteSpace(setting?.Address) ? DefaultAddress : setting!.Address!,
                string.IsNullOrWhiteSpace(setting?.Phone) ? DefaultPhone : PhoneDisplay.Pretty(setting!.Phone),
                mood,
                moodText,
                await _menuItems.Query().CountAsync(m => m.IsAvailable),
                await _categories.Query().CountAsync(),
                openDays.Count,
                lastOrders);
        }

        // One day's opening, the same way Member 2's booking rules work out a day (ReservationService.SessionFor):
        // a special date (or date range) replaces the weekly hours and has no break;
        // closing at or before the opening time means "after midnight".
        private sealed record DaySession(DateTime Open, DateTime Close, DateTime? BreakStart, DateTime? BreakEnd,
                                         bool Closed, string? SpecialName);

        private static DaySession SessionFor(DateTime date, List<OperatingHour> week, List<SpecialDate> specials)
        {
            date = date.Date;
            var sd = specials.FirstOrDefault(x => x.Date.Date <= date && (x.EndDate ?? x.Date).Date >= date);

            if (sd != null)
            {
                if (sd.IsClosed || !sd.OpenTime.HasValue || !sd.CloseTime.HasValue)
                    return new DaySession(date, date, null, null, true, sd.Name);

                var sOpen = date + sd.OpenTime.Value;
                var sClose = date + sd.CloseTime.Value;
                if (sClose <= sOpen) sClose = sClose.AddDays(1);
                return new DaySession(sOpen, sClose, null, null, false, sd.Name);
            }

            var h = week.First(x => x.Day == date.DayOfWeek);
            if (h.IsClosed) return new DaySession(date, date, null, null, true, null);

            var open = date + h.OpenTime.ToTimeSpan();
            var close = date + h.CloseTime.ToTimeSpan();
            if (close <= open) close = close.AddDays(1);

            DateTime? bs = null, be = null;
            if (h.BreakStart.HasValue && h.BreakEnd.HasValue)
            {
                var b1 = date + h.BreakStart.Value.ToTimeSpan();
                if (b1 < open) b1 = b1.AddDays(1);
                var b2 = date + h.BreakEnd.Value.ToTimeSpan();
                if (b2 <= b1) b2 = b2.AddDays(1);
                bs = b1; be = b2;
            }
            return new DaySession(open, close, bs, be, false, null);
        }

        // before / open / last / closed, plus the text for the little status pill.
        // A break shows as "before" (lantern off, dusk) with "On a break · back at 5:00 PM".
        private static (string Mood, string Text) WorkOutMood(List<OperatingHour> week, List<SpecialDate> specials, DateTime now)
        {
            // Still open from yesterday (late night after midnight), or today's session.
            foreach (var day in new[] { now.Date.AddDays(-1), now.Date })
            {
                var s = SessionFor(day, week, specials);
                if (s.Closed || now < s.Open || now >= s.Close) continue;

                if (s.BreakStart.HasValue && now >= s.BreakStart.Value && now < s.BreakEnd!.Value)
                    return ("before", $"On a short break · back at {Time(s.BreakEnd.Value)}");

                // The next stop is the break (if it is still ahead) or closing time.
                var breakAhead = s.BreakStart.HasValue && now < s.BreakStart.Value;
                var stop = breakAhead ? s.BreakStart!.Value : s.Close;
                var lastOrderAt = stop.AddMinutes(-LastOrderMinutes);
                var after = breakAhead
                    ? $"break {Time(s.BreakStart!.Value)} – {Time(s.BreakEnd!.Value)}"
                    : $"we close at {Time(s.Close)}";

                if (now >= lastOrderAt) return ("last", $"Last orders have closed · {after}");
                if (stop - now <= TimeSpan.FromHours(1)) return ("last", $"Last orders {Time(lastOrderAt)} · {after}");
                return ("open", breakAhead
                    ? $"We're open now · until {Time(stop)}, back at {Time(s.BreakEnd!.Value)}"
                    : $"We're open now · until {Time(s.Close)}");
            }

            var today = SessionFor(now.Date, week, specials);
            if (!today.Closed && now < today.Open)
            {
                var wait = today.Open - now;
                var inText = wait.Hours > 0 ? $"in {wait.Hours} h {wait.Minutes} min" : $"in {Math.Max(1, wait.Minutes)} min";
                return ("before", $"Opens today at {Time(today.Open)} · {inText}");
            }

            // Closed: find the next day we open (special dates included).
            var lead = today.SpecialName != null && today.Closed ? $"Closed today for {today.SpecialName}"
                     : today.Closed ? "Closed today"
                     : "Closed for tonight";
            for (var add = 1; add <= 14; add++)
            {
                var next = SessionFor(now.Date.AddDays(add), week, specials);
                if (next.Closed) continue;
                var when = add == 1 ? "tomorrow" : next.Open.DayOfWeek.ToString();
                return ("closed", $"{lead} · open {when} from {Time(next.Open)}");
            }
            return ("closed", $"{lead} · see you soon");
        }

        private Task<List<SpecialDate>> LoadSpecialsAsync() =>
            _specials.Query().AsNoTracking().ToListAsync();

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
            a.IsClosed == b.IsClosed && (a.IsClosed ||
                (a.OpenTime == b.OpenTime && a.CloseTime == b.CloseTime &&
                 a.BreakStart == b.BreakStart && a.BreakEnd == b.BreakEnd));

        private static string Time(TimeOnly t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);   // 17:00 -> "5:00 PM", 00:00 -> "12:00 AM"
        private static string Time(DateTime t) => Time(TimeOnly.FromDateTime(t));
        private static string IsoDate(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
