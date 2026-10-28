using System.Globalization;
using RROMS.Models;

namespace RROMS.Services;

// Owner: Member 3. Read Member 2's schedule without changing its model or data.
// Hours are restaurant-local Malaysian time (UTC+08:00), independent of server time.
public static class OrderOpeningHoursRules
{
    public const int LastOrderMinutes = 30;
    private static readonly TimeSpan RestaurantOffset = TimeSpan.FromHours(8);

    public static string? GetCheckoutError(DateTimeOffset utcNow,
        IReadOnlyCollection<OperatingHour> hours)
    {
        if (hours.Count == 0)
            return "Online ordering is unavailable because opening hours have not been configured. Please contact the restaurant.";

        DateTime now = utcNow.ToOffset(RestaurantOffset).DateTime;
        DateTime today = now.Date;
        var services = new List<(DateTime OpensAt, DateTime ClosesAt)>();

        // An overnight service belongs to the weekday on which it opened.
        // Check yesterday too, including when today's own schedule is closed.
        foreach (DateTime date in new[] { today.AddDays(-1), today })
        {
            var rows = hours.Where(hour => hour.Day == date.DayOfWeek).ToArray();
            if (rows.Length > 1)
                return "Online ordering is unavailable because the opening hours need checking. Please contact the restaurant.";

            var hour = rows.SingleOrDefault();
            if (hour is null || hour.IsClosed) continue;
            if (hour.OpenTime == hour.CloseTime)
                return "Online ordering is unavailable because the opening hours need checking. Please contact the restaurant.";

            DateTime opensAt = date + hour.OpenTime.ToTimeSpan();
            DateTime closesAt = date + hour.CloseTime.ToTimeSpan();
            if (closesAt < opensAt) closesAt = closesAt.AddDays(1);
            services.Add((opensAt, closesAt));
        }

        var active = services.Where(service => now >= service.OpensAt && now < service.ClosesAt)
            .OrderByDescending(service => service.OpensAt).ToArray();
        if (active.Length > 0)
        {
            var service = active[0];
            DateTime lastOrderAt = service.ClosesAt.AddMinutes(-LastOrderMinutes);
            if (now < lastOrderAt) return null;

            return $"Online ordering has closed. Last orders were at {Time(lastOrderAt)}; we close at {Time(service.ClosesAt)}. You can still browse the menu and view existing orders.";
        }

        var todayHours = hours.SingleOrDefault(hour => hour.Day == today.DayOfWeek);
        if (todayHours is null)
            return "Online ordering is unavailable because today's opening hours have not been configured. Please contact the restaurant.";
        if (todayHours.IsClosed)
            return "Online ordering is closed today. You can still browse the menu and view existing orders.";

        var upcoming = services.FirstOrDefault(service => service.OpensAt.Date == today && now < service.OpensAt);
        if (upcoming != default && upcoming.ClosesAt.AddMinutes(-LastOrderMinutes) > upcoming.OpensAt)
            return $"Online ordering opens at {Time(upcoming.OpensAt)}. You can still browse the menu and prepare your cart.";

        return "Online ordering is currently closed. You can still browse the menu and view existing orders.";
    }

    private static string Time(DateTime value)
        => value.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
}
