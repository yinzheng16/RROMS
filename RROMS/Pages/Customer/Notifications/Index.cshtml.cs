using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Notifications
{
    // US37 (Member 4): the customer's notification history.
    // Also serves the navbar bell: ?handler=Summary (unread count + latest 5, polled by notifications.js)
    // and ?handler=Open&id= (marks one read, then goes to the order / reservation / payment page).
    // The /Customer folder is CustomerOnly, so every handler only sees the signed-in customer's own data.
    public class IndexModel : PageModel
    {
        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        private readonly INotificationService _notifications;
        private readonly UserManager<ApplicationUser> _users;

        public IndexModel(INotificationService notifications, UserManager<ApplicationUser> users)
        {
            _notifications = notifications;
            _users = users;
        }

        // all | unread | orders | reservations | payments
        [BindProperty(SupportsGet = true)]
        public string? Show { get; set; }

        public List<NotificationItem> Items { get; private set; } = new();
        public int UnreadCount { get; private set; }

        public string CurrentShow => Show?.ToLowerInvariant() switch
        {
            "unread" or "orders" or "reservations" or "payments" => Show!.ToLowerInvariant(),
            _ => "all"
        };

        public async Task OnGetAsync()
        {
            var userId = _users.GetUserId(User)!;
            NotificationType? type = CurrentShow switch
            {
                "orders" => NotificationType.Order,
                "reservations" => NotificationType.Reservation,
                "payments" => NotificationType.Payment,
                _ => null
            };

            Items = await _notifications.GetItemsAsync(userId, take: 200, unreadOnly: CurrentShow == "unread", type: type);
            UnreadCount = await _notifications.CountUnreadAsync(userId);
        }

        public async Task<IActionResult> OnGetOpenAsync(int id)
        {
            var target = await _notifications.OpenAsync(id, _users.GetUserId(User)!);
            return LocalRedirect(Url.IsLocalUrl(target) ? target : "/Customer/Notifications");
        }

        public async Task<IActionResult> OnPostMarkAllAsync()
        {
            await _notifications.MarkAllAsReadAsync(_users.GetUserId(User)!);
            return RedirectToPage(new { show = CurrentShow == "all" ? null : CurrentShow });
        }

        // Bell dropdown + toasts. Small on purpose: polled every 30 seconds.
        public async Task<IActionResult> OnGetSummaryAsync()
        {
            var userId = _users.GetUserId(User)!;
            var latest = await _notifications.GetItemsAsync(userId, take: 5);
            var unread = await _notifications.CountUnreadAsync(userId);

            return new JsonResult(new
            {
                unread,
                items = latest.Select(n => new
                {
                    id = n.Id,
                    title = n.Title,
                    message = n.Message,
                    type = n.Type.ToString(),
                    isRead = n.IsRead,
                    createdAt = n.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss", En),
                    link = OpenLink(n.Id),
                    orderId = n.OrderId
                })
            });
        }

        public string OpenLink(int id) => Url.Page("/Customer/Notifications/Index", "Open", new { id })!;

        // ---------- display helpers (used by the .cshtml) ----------

        public static string Icon(NotificationType type, string title) => type switch
        {
            NotificationType.Order => title.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "bi-bag-x" : "bi-bag",
            NotificationType.Reservation => title.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "bi-calendar-x" : "bi-calendar-check",
            NotificationType.Payment => "bi-receipt",
            NotificationType.Promotion => "bi-megaphone",
            _ => "bi-info-circle"
        };

        public static string Tone(NotificationType type) => type switch
        {
            NotificationType.Order => "is-order",
            NotificationType.Payment => "is-payment",
            _ => "is-plain"
        };

        public static string DayHeading(DateTime day)
        {
            var today = DateTime.Today;
            if (day == today) return "Today";
            if (day == today.AddDays(-1)) return "Yesterday";
            return day.ToString(day.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", En);
        }

        // Small date after "Today" / "Yesterday"; older days already show the date.
        public static string? DayDate(DateTime day) =>
            day >= DateTime.Today.AddDays(-1) ? day.ToString("ddd d MMM", En) : null;
    }
}
