using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Tests;

// QA01 (Member 4): records the notifications a service asks to send.
// Throw = true simulates a broken notification service, to check that
// the caller's own work (e.g. a refund) is still saved.
public sealed class FakeNotificationService : INotificationService
{
    public sealed record Sent(string UserId, string Title, string Message, NotificationType Type);

    public List<Sent> SentNotifications { get; } = new();

    public bool Throw { get; set; }

    public Task SendAsync(string userId, string title, string message, NotificationType type) =>
        SendAsync(userId, title, message, type, null);

    public Task SendAsync(string userId, string title, string message, NotificationType type,
        NotificationEmail? email)
    {
        if (Throw)
            throw new InvalidOperationException("Notification service is down (test).");

        SentNotifications.Add(new Sent(userId, title, message, type));
        return Task.CompletedTask;
    }

    // Not used by the services under test.
    public Task<List<Notification>> GetForUserAsync(string userId, bool unreadOnly = false) =>
        throw new NotImplementedException();
    public Task<int> CountUnreadAsync(string userId) => throw new NotImplementedException();
    public Task MarkAsReadAsync(int notificationId, string userId) => throw new NotImplementedException();
    public Task NotifyOrderStatusAsync(int orderId, OrderStatus newStatus) => throw new NotImplementedException();
    public Task<List<NotificationItem>> GetItemsAsync(string userId, int? take = null,
        bool unreadOnly = false, NotificationType? type = null) => throw new NotImplementedException();
    public Task<List<int>> MarkAllAsReadAsync(string userId) => throw new NotImplementedException();
    public Task MarkAsUnreadAsync(string userId, IEnumerable<int> notificationIds) =>
        throw new NotImplementedException();
    public Task<string> OpenAsync(int notificationId, string userId) => throw new NotImplementedException();
}
