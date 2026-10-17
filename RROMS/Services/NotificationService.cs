using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // EN06 starter implementation: saves in-app notifications to the database.
    // Owner after Sprint 0: Member 4 (adds email and SignalR push here).
    public class NotificationService : INotificationService
    {
        private readonly IRepository<Notification> _notifications;

        public NotificationService(IRepository<Notification> notifications)
        {
            _notifications = notifications;
        }

        public async Task SendAsync(string userId, string title, string message, NotificationType type)
        {
            await _notifications.AddAsync(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type
            });
            await _notifications.SaveChangesAsync();
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
    }
}
