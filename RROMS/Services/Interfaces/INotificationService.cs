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
    }

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
