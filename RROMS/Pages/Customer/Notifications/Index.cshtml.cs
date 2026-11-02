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

        // Ids just marked by "Mark all as read"; while set, the button offers "Mark all as unread".
        public List<int> UndoIds { get; private set; } = new();

        private const string UndoKey = "NtfUndo";

        // notifications.js (bell dropdown) asks for JSON instead of a page redirect.
        private bool WantsJson => Request.Headers.Accept.ToString().Contains("application/json");

        private object? ShowRoute => new { show = CurrentShow == "all" ? null : CurrentShow };

        private static List<int> ParseIds(string? text) =>
            (text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

        // Ids the bell dropdown just marked as read (its "View all notices" link carries them),
        // so the page can still offer "Mark all as unread".
        [BindProperty(SupportsGet = true)]
        public string? Undo { get; set; }

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
            // Right after "Mark all as read" on this page (TempData, shown once) or in the bell (?undo=).
            UndoIds = TempData[UndoKey] is string undo ? ParseIds(undo) : ParseIds(Undo);
        }

        public async Task<IActionResult> OnGetOpenAsync(int id)
        {
            var target = await _notifications.OpenAsync(id, _users.GetUserId(User)!);
            return LocalRedirect(Url.IsLocalUrl(target) ? target : "/Customer/Notifications");
        }

        public async Task<IActionResult> OnPostMarkAllAsync()
        {
            var ids = await _notifications.MarkAllAsReadAsync(_users.GetUserId(User)!);
            if (WantsJson) return new JsonResult(new { ids });

            if (ids.Count is > 0 and <= 300) TempData[UndoKey] = string.Join(",", ids);
            return RedirectToPage(ShowRoute);
        }

        // "Mark all as unread": puts back the ids that "Mark all as read" returned.
        public async Task<IActionResult> OnPostMarkUnreadAsync([FromForm] string? ids)
        {
            await _notifications.MarkAsUnreadAsync(_users.GetUserId(User)!, ParseIds(ids));
            if (WantsJson) return new JsonResult(new { ok = true });
            return RedirectToPage(ShowRoute);
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
                    icon = Icon(n.Type, n.Title),
                    tone = Tone(n.Type),
                    isRead = n.IsRead,
                    createdAt = n.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss", En),
                    link = OpenLink(n.Id),
                    orderId = n.OrderId
                })
            });
        }

        public string OpenLink(int id) => Url.Page("/Customer/Notifications/Index", "Open", new { id })!;

        // ---------- display helpers (used by the .cshtml) ----------

        // Circle colour = type (Tone); icon inside = the status, read from the title
        // (e.g. "Order being prepared" -> flame). Unknown titles get the type's plain icon.
        // Also sent to notifications.js in the Summary JSON, so the bell uses the same icons.
        public static string Icon(NotificationType type, string title)
        {
            var t = (title ?? string.Empty).ToLowerInvariant();
            bool Has(string word) => t.Contains(word);

            return type switch
            {
                NotificationType.Order =>
                    Has("cancel") ? "bi-bag-x" :
                    Has("pickup") ? "bi-bag-check" :
                    Has("prepar") ? "bi-fire" :
                    Has("ready") ? "bi-arrow-right-circle" :      // dine-in: on its way to the table
                    Has("served") ? "bi-cup-hot" :
                    Has("complete") ? "bi-check2-circle" :
                    "bi-bag",                                      // received / other

                NotificationType.Reservation =>
                    Has("cancel") || Has("no-show") || Has("no show") ? "bi-calendar-x" :
                    Has("confirm") ? "bi-calendar-check" :
                    Has("remind") ? "bi-alarm" :
                    Has("seat") ? "bi-door-open" :
                    Has("updat") || Has("chang") ? "bi-calendar-event" :
                    Has("receiv") || Has("book") || Has("request") ? "bi-calendar-plus" :
                    "bi-calendar",

                NotificationType.Payment =>
                    Has("fail") || Has("declin") ? "bi-exclamation-circle" :
                    Has("refund") ? "bi-arrow-counterclockwise" :
                    Has("pending") || Has("waiting") ? "bi-hourglass-split" :
                    Has("receiv") || Has("paid") || Has("success") ? "bi-receipt" :
                    "bi-credit-card",

                NotificationType.Promotion => "bi-megaphone",
                _ => Has("password") || Has("security") || Has("email") ? "bi-shield-check" : "bi-info-circle"
            };
        }

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
