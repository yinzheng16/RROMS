using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RROMS.Hubs;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // EN06 starter implementation: saves in-app notifications to the database.
    // Owner after Sprint 0: Member 4 (adds email and SignalR push here).
    //
    // US38 (Member 4): email notifications.
    //   - Every notification is saved in-app first (unchanged).
    //   - Then an email is sent through the shared IEmailSender (EN07) in the YORU layout (EmailTemplates):
    //       Reservation -> detailed booking email (reference, date, time, guests, table, ...)
    //       Order       -> detailed order email, only for the important moments (see BuildOrderEmailAsync)
    //       Payment     -> simple email from the title/message (or the detailed overload)
    //       Promotion / System -> only when the caller passes a NotificationEmail
    //   - A failed email is logged and never throws, so a booking / payment is never undone because of email.
    public class NotificationService : INotificationService
    {
        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        private readonly IRepository<Notification> _notifications;
        private readonly IRepository<Reservation> _reservations;
        private readonly IRepository<Order> _orders;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IEmailSender _email;
        private readonly IHomeService _home;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<NotificationService> _logger;
        private readonly IHubContext<NotificationHub> _hub;   // US37: real-time push to the bell
        private readonly IServiceScopeFactory _scopes;         // US38: emails are sent in the background

        // Site address for email buttons when the email is built outside the web request (background send).
        private string? _siteBase;

        public NotificationService(
            IRepository<Notification> notifications,
            IRepository<Reservation> reservations,
            IRepository<Order> orders,
            UserManager<ApplicationUser> users,
            IEmailSender email,
            IHomeService home,
            IHttpContextAccessor http,
            ILogger<NotificationService> logger,
            IHubContext<NotificationHub> hub,
            IServiceScopeFactory scopes)
        {
            _notifications = notifications;
            _reservations = reservations;
            _orders = orders;
            _users = users;
            _email = email;
            _home = home;
            _http = http;
            _logger = logger;
            _hub = hub;
            _scopes = scopes;
        }

        public Task SendAsync(string userId, string title, string message, NotificationType type) =>
            SendAsync(userId, title, message, type, null);

        public async Task SendAsync(string userId, string title, string message, NotificationType type,
            NotificationEmail? email)
        {
            // US37: a notification is extra; it must never undo or break the caller's work
            // (a booking, an order status change, a payment), so a save failure is only logged.
            try
            {
                await _notifications.AddAsync(new Notification
                {
                    UserId = userId,
                    Title = Cut(title, 100),
                    Message = Cut(message, 500),
                    Type = type
                });
                await _notifications.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "In-app notification for user {UserId} ({Title}) was not saved.", userId, title);
                return;
            }

            // US37: tell the customer's open pages straight away (bell badge + toast).
            // Only a signal is sent; if it fails, the bell still finds the notice on its 30-second check.
            try
            {
                await _hub.Clients.User(userId).SendAsync(NotificationHub.ReceivedEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live push for user {UserId} ({Title}) was not sent.", userId, title);
            }

            // US38: the in-app notification is already saved; the email is extra and must never fail the caller.
            // It is sent in the background, so the caller (e.g. staff pressing "Ready") does not wait for Gmail.
            // A fresh scope gives it its own database context, because the request's one is gone by then.
            var siteBase = CurrentSiteBase();
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var worker = (NotificationService)scope.ServiceProvider.GetRequiredService<INotificationService>();
                    worker._siteBase = siteBase;
                    await worker.SendEmailAsync(userId, title, message, type, email);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Notification email for user {UserId} ({Title}) was not sent.", userId, title);
                }
            });
        }

        // "https://localhost:7223" from the current request; null when there is no request.
        private string? CurrentSiteBase()
        {
            var request = _http.HttpContext?.Request;
            return request is null ? null : $"{request.Scheme}://{request.Host}{request.PathBase}";
        }

        public Task<List<Notification>> GetForUserAsync(string userId, bool unreadOnly = false) =>
            _notifications.Query()
                .AsNoTracking()
                .Where(n => n.UserId == userId && (!unreadOnly || !n.IsRead))
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

        public Task<int> CountUnreadAsync(string userId) =>
            _notifications.Query().CountAsync(n => n.UserId == userId && !n.IsRead);

        public async Task MarkAsReadAsync(int notificationId, string userId)
        {
            var n = await _notifications.Query()
                .FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId);
            if (n == null) return;

            n.IsRead = true;
            await _notifications.SaveChangesAsync();
        }

        // ======================= US37 in-app notifications =======================

        public async Task NotifyOrderStatusAsync(int orderId, OrderStatus newStatus)
        {
            try
            {
                var order = await _orders.Query()
                    .AsNoTracking()
                    .Where(o => o.Id == orderId)
                    .Select(o => new { o.UserId, o.OrderNo, o.OrderType })
                    .FirstOrDefaultAsync();
                if (order is null || string.IsNullOrEmpty(order.UserId)) return;

                var text = OrderStatusText(order.OrderNo, order.OrderType == OrderType.Takeaway, newStatus);
                if (text is null) return;

                await SendAsync(order.UserId, text.Value.Title, text.Value.Message, NotificationType.Order);
            }
            catch (Exception ex)
            {
                // The status is already saved by the caller; a missing notification must not undo it.
                _logger.LogWarning(ex, "Order status notification for order {OrderId} ({Status}) was not sent.", orderId, newStatus);
            }
        }

        // The order number stays in the message: US38 finds the order for the email from it,
        // and US37 uses it to open the right order.
        private static (string Title, string Message)? OrderStatusText(string orderNo, bool takeaway, OrderStatus status) =>
            status switch
            {
                OrderStatus.Pending => ("Order received", $"We have received your order {orderNo}."),
                OrderStatus.Preparing => ("Order being prepared", $"The kitchen has started your order {orderNo}."),
                OrderStatus.ReadyToServe => takeaway
                    ? ("Order ready for pickup", $"Your takeaway order {orderNo} is ready at the counter.")
                    : ("Order ready", $"Your order {orderNo} is ready and on its way to your table."),
                OrderStatus.Served => ("Order served", $"Your order {orderNo} has been served. Enjoy your meal!"),
                OrderStatus.Completed => ("Order completed", $"Your order {orderNo} is complete. Thank you for dining with us."),
                OrderStatus.Cancelled => ("Order cancelled", $"Your order {orderNo} was cancelled."),
                _ => null
            };

        public async Task<List<NotificationItem>> GetItemsAsync(string userId, int? take = null,
            bool unreadOnly = false, NotificationType? type = null)
        {
            var query = _notifications.Query()
                .AsNoTracking()
                .Where(n => n.UserId == userId && (!unreadOnly || !n.IsRead));
            if (type is not null) query = query.Where(n => n.Type == type);
            query = query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id);
            if (take is not null) query = query.Take(take.Value);

            var list = await query.ToListAsync();
            if (list.Count == 0) return new List<NotificationItem>();

            var refs = await LoadRefsAsync(userId, list);
            return list.Select(n => ToItem(n, refs)).ToList();
        }

        public async Task<List<int>> MarkAllAsReadAsync(string userId)
        {
            var unread = await _notifications.Query()
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();
            if (unread.Count == 0) return new List<int>();

            foreach (var n in unread) n.IsRead = true;
            await _notifications.SaveChangesAsync();
            return unread.Select(n => n.Id).ToList();
        }

        public async Task MarkAsUnreadAsync(string userId, IEnumerable<int> notificationIds)
        {
            var ids = notificationIds.Where(id => id > 0).Distinct().Take(500).ToList();
            if (ids.Count == 0) return;

            var rows = await _notifications.Query()
                .Where(n => n.UserId == userId && n.IsRead && ids.Contains(n.Id))
                .ToListAsync();
            if (rows.Count == 0) return;

            foreach (var n in rows) n.IsRead = false;
            await _notifications.SaveChangesAsync();
        }

        public async Task<string> OpenAsync(int notificationId, string userId)
        {
            var n = await _notifications.Query()
                .FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId);
            if (n is null) return NotificationsPage;

            if (!n.IsRead)
            {
                n.IsRead = true;
                await _notifications.SaveChangesAsync();
            }

            var refs = await LoadRefsAsync(userId, new List<Notification> { n });
            return ToItem(n, refs).TargetPath;
        }

        private const string NotificationsPage = "/Customer/Notifications";

        // The customer's order numbers and reservation references, loaded once per list
        // (only when the list has order / reservation notifications).
        private sealed record Refs(List<(int Id, string OrderNo)> Orders, List<string> Reservations);

        private async Task<Refs> LoadRefsAsync(string userId, List<Notification> list)
        {
            var orders = new List<(int, string)>();
            var reservations = new List<string>();

            if (list.Any(n => n.Type == NotificationType.Order))
            {
                orders = (await _orders.Query()
                        .AsNoTracking()
                        .Where(o => o.UserId == userId && o.OrderNo != "")
                        .Select(o => new { o.Id, o.OrderNo })
                        .ToListAsync())
                    .Select(o => (o.Id, o.OrderNo))
                    .ToList();
            }

            if (list.Any(n => n.Type == NotificationType.Reservation))
            {
                reservations = await _reservations.Query()
                    .AsNoTracking()
                    .Where(r => r.UserId == userId && r.ReferenceNo != "")
                    .Select(r => r.ReferenceNo)
                    .ToListAsync();
            }

            return new Refs(orders, reservations);
        }

        private static NotificationItem ToItem(Notification n, Refs refs)
        {
            string target;
            int? orderId = null;
            string? refLabel = null, refValue = null;

            switch (n.Type)
            {
                case NotificationType.Order:
                    // Longest match first, so "YR-12" never matches the message of "YR-123".
                    var order = refs.Orders
                        .Where(o => n.Message.Contains(o.OrderNo))
                        .OrderByDescending(o => o.OrderNo.Length)
                        .Select(o => ((int Id, string OrderNo)?)o)
                        .FirstOrDefault();
                    if (order is not null)
                    {
                        orderId = order.Value.Id;
                        refLabel = "Order";
                        refValue = order.Value.OrderNo;
                        target = $"/Customer/OrderConfirmation?id={order.Value.Id}";
                    }
                    else target = NotificationsPage;   // order not found (e.g. very old): stay on the history page
                    break;

                case NotificationType.Reservation:
                    refValue = refs.Reservations
                        .Where(r => n.Message.Contains(r))
                        .OrderByDescending(r => r.Length)
                        .FirstOrDefault();
                    if (refValue is not null) refLabel = "Reference";
                    target = "/Customer/Reservations";
                    break;

                case NotificationType.Payment:
                    var bill = System.Text.RegularExpressions.Regex.Match(n.Message, @"\bYB-\d+\b");
                    if (bill.Success) { refLabel = "Bill"; refValue = bill.Value; }
                    target = "/Customer/Payments";
                    break;

                case NotificationType.Promotion:
                    target = "/";
                    break;

                default:
                    target = NotificationsPage;
                    break;
            }

            return new NotificationItem(n.Id, n.Title, n.Message, n.Type, n.IsRead, n.CreatedAt,
                target, orderId, refLabel, refValue);
        }

        private static string Cut(string text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..(max - 1)] + "…";

        // ======================= US38 email =======================

        private async Task SendEmailAsync(string userId, string title, string message, NotificationType type,
            NotificationEmail? email)
        {
            // Promotion / System: only when the caller asks for an email.
            if (email is null && type is not (NotificationType.Reservation or NotificationType.Order or NotificationType.Payment))
                return;

            var user = await _users.FindByIdAsync(userId);
            if (user is null || string.IsNullOrWhiteSpace(user.Email)) return;

            var footer = await FooterAsync();

            // Reservation: a detailed email for received / confirmed / updated / cancelled only;
            // other moments (seated, completed, no-show) stay in-app.
            if (email is null && type == NotificationType.Reservation)
            {
                var reservationEmail = await BuildReservationEmailAsync(user, title, message, footer);
                if (reservationEmail is not null)
                    await _email.SendEmailAsync(user.Email, reservationEmail.Value.Subject, reservationEmail.Value.Html);
                return;
            }

            // Order: a detailed email for the important moments only; other status changes stay in-app.
            if (email is null && type == NotificationType.Order)
            {
                var orderEmail = await BuildOrderEmailAsync(user, message, footer);
                if (orderEmail is not null)
                    await _email.SendEmailAsync(user.Email, orderEmail.Value.Subject, orderEmail.Value.Html);
                return;
            }

            // Detailed email from the caller, or a simple one from the notification text.
            email ??= new NotificationEmail(
                Subject: $"YORU · {title}",
                Kicker: KickerFor(type),
                Title: title);

            var html = EmailTemplates.Build(
                kicker: email.Kicker,
                title: email.Title,
                name: user.FullName,
                introHtml: EmailTemplates.Encode(email.IntroText ?? message),
                rows: email.Rows,
                buttonText: email.ButtonText,
                buttonUrl: AbsoluteUrl(email.ButtonPath),
                smallText: email.SmallText ?? "You get this email because you have a YORU account.",
                footerLine: footer,
                buttonFullWidth: true);

            await _email.SendEmailAsync(user.Email, email.Subject, html);
        }

        // Reservation emails show the booking itself. The callers (Member 2's pages and StaffReservationService)
        // put the reference number in the message, so the booking is found from it. The booking's CURRENT
        // status (and the title, for a change of date/time/guests) decides the email:
        //   Pending -> received, Confirmed -> confirmed, Cancelled -> cancelled,
        //   title "Reservation modified" / "Reservation updated" (still Pending/Confirmed) -> updated,
        //   Seated / Completed / NoShow -> no email (in-app only).
        private async Task<(string Subject, string Html)?> BuildReservationEmailAsync(
            ApplicationUser user, string notificationTitle, string message, string footer)
        {
            var recent = await _reservations.Query()
                .AsNoTracking()
                .Include(r => r.Table!).ThenInclude(t => t.TableTypeDefinition)
                .Include(r => r.Table!).ThenInclude(t => t.DiningArea)
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.CreatedAt)
                .Take(30)
                .ToListAsync();

            var r = recent.FirstOrDefault(x => !string.IsNullOrEmpty(x.ReferenceNo) && message.Contains(x.ReferenceNo));
            if (r is null) return null;

            // Seated, completed and no-show are only shown in-app (the guest is at the restaurant or the visit is over).
            if (r.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Cancelled))
                return null;

            bool changed = r.Status != ReservationStatus.Cancelled &&
                (notificationTitle.Contains("modified", StringComparison.OrdinalIgnoreCase) ||
                 notificationTitle.Contains("updated", StringComparison.OrdinalIgnoreCase));

            var rows = new List<EmailTemplates.Row>
            {
                new("Reference", r.ReferenceNo),
                new("Date", r.ReservedAt.ToString("ddd, d MMM yyyy", En)),
                new("Time", r.ReservedAt.ToString("h:mm tt", En)),
                new("Guests", r.Guests == 1 ? "1 guest" : $"{r.Guests} guests")
            };

            var typeDef = r.Table?.TableTypeDefinition;
            if (typeDef is not null)
            {
                var tableName = string.IsNullOrWhiteSpace(typeDef.JapaneseName)
                    ? typeDef.Name
                    : $"{typeDef.Name} · {typeDef.JapaneseName}";
                rows.Add(new("Table", tableName));
            }
            if (!string.IsNullOrWhiteSpace(r.Table?.DiningArea?.Name))
                rows.Add(new("Area", r.Table!.DiningArea.Name));
            if (r.MinimumSpendAccepted is decimal spend && spend > 0m)
                rows.Add(new("Minimum spend", $"RM {spend.ToString("N2", En)}"));
            if (!string.IsNullOrWhiteSpace(r.SpecialRequest))
                rows.Add(new("Special request", r.SpecialRequest));

            string subject, kicker, title, intro, small;
            string? warn = null;

            switch (r.Status)
            {
                case not ReservationStatus.Cancelled when changed:
                    subject = $"Your YORU booking {r.ReferenceNo} was updated";
                    kicker = "Reservation updated";
                    title = "Your booking was updated";
                    intro = r.Status == ReservationStatus.Confirmed
                        ? "Your booking has been changed and your table stays confirmed. Here are the new details."
                        : "Your booking has been changed. Here are the new details. It is waiting for the restaurant to confirm.";
                    small = "Need to change plans again? You can manage your booking from My Reservations.";
                    break;

                case ReservationStatus.Cancelled:
                    subject = $"Your YORU booking {r.ReferenceNo} was cancelled";
                    kicker = "Reservation cancelled";
                    title = "Your booking was cancelled";
                    intro = "Your reservation below has been cancelled. We hope to welcome you another time.";
                    small = "Want to come another day? You can book a new table any time.";
                    break;

                case ReservationStatus.Confirmed:
                    subject = $"Your YORU booking {r.ReferenceNo} is confirmed";
                    kicker = "Reservation confirmed";
                    title = "See you soon!";
                    intro = "Your table is confirmed. Here are your booking details.";
                    small = "Please arrive on time. Need to change plans? Cancel from My Reservations.";
                    break;

                default: // Pending (just booked)
                    subject = $"We've received your YORU booking {r.ReferenceNo}";
                    kicker = "Reservation received";
                    title = "Thanks for your booking";
                    intro = "We've received your reservation. It is waiting for the restaurant to confirm, and we'll let you know.";
                    small = "Need to change plans? You can cancel from My Reservations.";
                    break;
            }

            var phone = await PhoneAsync();
            if (r.Status != ReservationStatus.Cancelled && !string.IsNullOrWhiteSpace(phone))
                warn = $"<b>Running late?</b> Call us at {EmailTemplates.Encode(phone)}.";

            var html = EmailTemplates.Build(
                kicker: kicker,
                title: title,
                name: user.FullName,
                introHtml: EmailTemplates.Encode(intro),
                rows: rows,
                warnHtml: warn,
                buttonText: r.Status == ReservationStatus.Cancelled ? "Book a table" : "View my reservations",
                buttonUrl: AbsoluteUrl(r.Status == ReservationStatus.Cancelled
                    ? "/Customer/Reservations/Create"
                    : "/Customer/Reservations"),
                smallText: small,
                footerLine: footer,
                buttonFullWidth: true);

            return (subject, html);
        }

        // Order emails show the order itself. The caller (Member 3, after saving the new status) puts the
        // order number in the message; the order's CURRENT status decides the email:
        //   Pending (takeaway)      -> "We've received your order"
        //   ReadyToServe (takeaway) -> "Ready for pickup"
        //   Cancelled (any)         -> "Your order was cancelled"
        // Anything else (Preparing, Served, Completed, dine-in rounds) -> no email, in-app only.
        private async Task<(string Subject, string Html)?> BuildOrderEmailAsync(
            ApplicationUser user, string message, string footer)
        {
            var recent = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items).ThenInclude(i => i.MenuItem)
                .Include(o => o.Payments)
                .Include(o => o.Bill)
                .Where(o => o.UserId == user.Id)
                .OrderByDescending(o => o.CreatedAt)
                .Take(30)
                .ToListAsync();

            // Longest match first, so "YR-12" never matches the message of "YR-123".
            var order = recent
                .Where(x => !string.IsNullOrEmpty(x.OrderNo) && message.Contains(x.OrderNo))
                .OrderByDescending(x => x.OrderNo.Length)
                .FirstOrDefault();
            if (order is null) return null;

            bool takeaway = order.OrderType == OrderType.Takeaway;
            bool paid = order.Bill?.Status == BillStatus.Paid || order.Payments.Any(p => p.Status == PaymentStatus.Paid);

            string subject, kicker, title, intro, small;
            string? warn = null;

            switch (order.Status)
            {
                case OrderStatus.Pending when takeaway:
                    subject = $"We've received your YORU order {order.OrderNo}";
                    kicker = "Order received";
                    title = "Thanks for your order";
                    intro = "We've received your takeaway order and the kitchen will start on it soon. We'll email you again when it's ready for pickup.";
                    small = "You can follow your order status on our website.";
                    break;

                case OrderStatus.ReadyToServe when takeaway:
                    subject = $"Your YORU order {order.OrderNo} is ready for pickup";
                    kicker = "Ready for pickup";
                    title = "Your order is ready!";
                    intro = "Your takeaway order is ready. Please collect it at the counter.";
                    small = "Please show your order number at the counter.";
                    if (!paid)
                        warn = "<b>Not paid yet?</b> You can pay at the counter when you collect your order.";
                    break;

                case OrderStatus.Cancelled:
                    subject = $"Your YORU order {order.OrderNo} was cancelled";
                    kicker = "Order cancelled";
                    title = "Your order was cancelled";
                    intro = "Your order below has been cancelled.";
                    small = "Questions about this order? Contact us and we'll help.";
                    if (paid)
                        warn = "<b>Already paid?</b> Please contact us about your payment.";
                    break;

                default:
                    return null;
            }

            var rows = new List<EmailTemplates.Row>
            {
                new("Order", order.OrderNo),
                new("Type", takeaway ? "Takeaway" : "Dine-in"),
                new("Placed", order.CreatedAt.ToString("ddd, d MMM yyyy · h:mm tt", En))
            };

            const int maxItems = 10;
            var items = order.Items.OrderBy(i => i.Id).ToList();
            foreach (var item in items.Take(maxItems))
                rows.Add(new($"{item.Quantity} × {item.MenuItem?.Name ?? "Item"}", Money(item.Quantity * item.UnitPrice)));
            if (items.Count > maxItems)
                rows.Add(new($"+ {items.Count - maxItems} more item(s)", ""));

            rows.Add(new("Subtotal", Money(order.Subtotal)));
            if (order.ServiceCharge > 0) rows.Add(new("Service charge", Money(order.ServiceCharge)));
            if (order.TaxAmount > 0) rows.Add(new("Tax", Money(order.TaxAmount)));
            rows.Add(new("Total", Money(order.TotalAmount)));
            if (order.Status != OrderStatus.Cancelled)
                rows.Add(new("Payment", paid ? "Paid" : "Not paid yet"));

            var html = EmailTemplates.Build(
                kicker: kicker,
                title: title,
                name: user.FullName,
                introHtml: EmailTemplates.Encode(intro),
                rows: rows,
                warnHtml: warn,
                buttonText: "View my order",
                buttonUrl: AbsoluteUrl($"/Customer/OrderConfirmation?id={order.Id}"),
                smallText: small,
                footerLine: footer,
                buttonFullWidth: true);

            return (subject, html);
        }

        private static string Money(decimal amount) => $"RM {amount.ToString("N2", En)}";

        private static string KickerFor(NotificationType type) => type switch
        {
            NotificationType.Reservation => "Reservation",
            NotificationType.Order => "Your order",
            NotificationType.Payment => "Payment",
            NotificationType.Promotion => "From YORU",
            _ => "YORU"
        };

        // "/Customer/Reservations" -> "https://localhost:7223/Customer/Reservations".
        // No web request (e.g. a background job) -> no button.
        private string? AbsoluteUrl(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;

            var site = _siteBase ?? CurrentSiteBase();
            if (site is null) return null;
            return $"{site}{(path.StartsWith('/') ? path : "/" + path)}";
        }

        private async Task<HomeInfo?> SafeHomeAsync()
        {
            try { return await _home.GetInfoAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not read restaurant details for the email footer."); return null; }
        }

        private async Task<string?> PhoneAsync() => (await SafeHomeAsync())?.Phone;

        // Same footer as Member 1's security emails: name · address · phone.
        private async Task<string> FooterAsync()
        {
            var info = await SafeHomeAsync();
            if (info is null) return Brand.FullName;
            var address = info.Address.Replace("\r", "").Replace("\n", ", ");
            return $"{Brand.FullName} · {address} · {info.Phone}";
        }
    }
}
