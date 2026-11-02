using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // EN06: shared notification contract.
    // Any module can send a notification without knowing how it is delivered.
    // Member 4 extends the implementation with email (MailKit) and real-time (SignalR).
    public interface INotificationService
    {
        // Saves the in-app notification. US38 also emails the customer automatically:
        //   Reservation -> booking details (put the reservation's ReferenceNo in the message)
        //   Order       -> order details, only for: takeaway order received, takeaway ready for pickup,
        //                  order cancelled (put the order's OrderNo in the message; call AFTER saving the status)
        //   Payment     -> simple email from the title/message
        //   Promotion / System -> no email unless you use the overload below.
        Task SendAsync(string userId, string title, string message, NotificationType type);

        // US38: same as above, plus a detailed email built from `email` (any type).
        // Pass null to behave exactly like the 4-parameter version.
        Task SendAsync(string userId, string title, string message, NotificationType type, NotificationEmail? email);

        Task<List<Notification>> GetForUserAsync(string userId, bool unreadOnly = false);

        Task<int> CountUnreadAsync(string userId);

        Task MarkAsReadAsync(int notificationId, string userId);

        // ======================= US37 in-app notifications =======================

        // Member 3: call AFTER the new order status is saved. Never throws.
        // The customer, order number and order type are read from the order itself,
        // so only the order's owner is notified. Title and message are written here.
        Task NotifyOrderStatusAsync(int orderId, OrderStatus newStatus);

        // Bell dropdown + Notifications page: newest first, with where each one leads.
        // take = null: all; type = null: every type.
        Task<List<NotificationItem>> GetItemsAsync(string userId, int? take = null,
            bool unreadOnly = false, NotificationType? type = null);

        Task MarkAllAsReadAsync(string userId);

        // Marks the notification read and returns the page it leads to
        // (the order, My Reservations, My Payments ...). Only the owner's own notifications.
        Task<string> OpenAsync(int notificationId, string userId);
    }

    // US37: one notification as shown in the bell and on the Notifications page.
    // TargetPath: where "View" leads. OrderId: set for order notifications (used to avoid
    // a second toast on the order page that already shows the live status).
    // RefLabel / RefValue: e.g. "Order" / "YR2611...", "Reference" / "RS-0042", "Bill" / "YB-000012".
    public record NotificationItem(
        int Id,
        string Title,
        string Message,
        NotificationType Type,
        bool IsRead,
        DateTime CreatedAt,
        string TargetPath,
        int? OrderId,
        string? RefLabel,
        string? RefValue);

    // US38 (Member 4): what to put in the email. Uses the shared YORU layout (EmailTemplates.Build).
    // Example (Member 3, takeaway ready):
    //   await _notifications.SendAsync(userId, "Order ready", "Your order ORD-0012 is ready for pickup.",
    //       NotificationType.Order,
    //       new NotificationEmail("Your YORU order is ready", "Takeaway", "Your order is ready for pickup",
    //           Rows: new[] { new EmailTemplates.Row("Order", "ORD-0012") }));
    public record NotificationEmail(
        string Subject,
        string Kicker,
        string Title,
        string? IntroText = null,                       // plain text; null = the notification message
        IEnumerable<EmailTemplates.Row>? Rows = null,
        string? ButtonText = null,
        string? ButtonPath = null,                      // site path, e.g. "/Customer/Reservations"
        string? SmallText = null);
}
