using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // EN06: shared notification contract.
    // Any module can send a notification without knowing how it is delivered.
    // Member 4 extends the implementation with email (MailKit) and real-time (SignalR).
    public interface INotificationService
    {
        Task SendAsync(string userId, string title, string message, NotificationType type);

        Task<List<Notification>> GetForUserAsync(string userId, bool unreadOnly = false);

        Task<int> CountUnreadAsync(string userId);

        Task MarkAsReadAsync(int notificationId, string userId);
    }
}
